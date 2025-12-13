import express from 'express';
import http from 'http';
import { Server, Socket } from 'socket.io';
import cors from 'cors';
import { ChannelRegistry } from './services/ChannelRegistry';
import { GroupRegistry } from './services/GroupRegistry';
import { RobloxAvatarService } from './services/RobloxAvatarService';
import axios from 'axios';

const app = express();
app.use(cors());
app.use(express.json());

const server = http.createServer(app);
const io = new Server(server, {
    cors: {
        origin: "*",
        methods: ["GET", "POST"]
    },
    pingTimeout: 60000,
    pingInterval: 25000
});

const registry = new ChannelRegistry();
const groupRegistry = new GroupRegistry();
const avatarService = new RobloxAvatarService();
const disconnectTimeouts = new Map<string, any>();
const userSockets = new Map<number, string>(); // UserId -> SocketId

io.on('connection', (socket: Socket) => {
    console.log('A user connected:', socket.id);

    socket.on('joinChannel', async (data: { jobId: string, username: string, userId?: number, placeId?: number }) => {
        const { jobId, username, userId, placeId } = data;
        if (!jobId || !username) return;

        if (userId) {
            userSockets.set(userId, socket.id);
        }

        const previousJobId = (socket as any).jobId;
        const previousUsername = (socket as any).username;

        if (previousJobId && previousUsername && (previousJobId !== jobId || previousUsername !== username)) {
            registry.removeParticipant(previousJobId, previousUsername);
            socket.leave(previousJobId);
            
            io.to(previousJobId).emit('participantsChanged', {
                jobId: previousJobId,
                participants: registry.getParticipants(previousJobId)
            });
        }

        const key = `${jobId}:${username}`;
        if (disconnectTimeouts.has(key)) {
            clearTimeout(disconnectTimeouts.get(key));
            disconnectTimeouts.delete(key);
        }

        let avatarUrl: string | undefined = undefined;
        if (userId) {
            const url = await avatarService.tryGetHeadshotUrl(userId);
            if (url) avatarUrl = url;
        }

        const channel = registry.createOrGetChannel(jobId, username, userId, avatarUrl, placeId);
        
        socket.join(jobId);
        
        // Store session info on socket
        (socket as any).jobId = jobId;
        (socket as any).username = username;
        if (userId) (socket as any).userId = userId;

        // Send snapshot to caller
        socket.emit('channelSnapshot', {
            jobId: channel.jobId,
            createdAt: channel.createdAt,
            createdBy: channel.createdBy,
            history: channel.getHistory(),
            participants: channel.getParticipants()
        });

        // Notify others
        io.to(jobId).emit('participantsChanged', {
            jobId,
            participants: channel.getParticipants()
        });
    });

    socket.on('searchUsers', async (query: string) => {
        if (!query || query.length < 1) {
            socket.emit('searchResults', []);
            return;
        }
        
        const jobId = (socket as any).jobId;
        const results = registry.searchUsers(query, jobId);
        socket.emit('searchResults', results);
    });

    socket.on('getGames', async () => {
        // Filter out negative Job IDs (DMs) - handled in registry
        const games = registry.getGames();
        const placeIds = [...new Set(games.map(g => g.placeId))];

        if (placeIds.length > 0) {
            try {
                // Start fetching thumbnails
                const thumbPromise = axios.get('https://thumbnails.roblox.com/v1/places/gameicons', {
                    params: {
                        placeIds: placeIds.join(','),
                        returnPolicy: 'PlaceHolder',
                        size: '150x150',
                        format: 'Png',
                        isCircular: false
                    }
                }).catch((e: any) => null);

                // Start fetching universe IDs
                const universePromises = placeIds.map(pid => 
                    axios.get(`https://apis.roblox.com/universes/v1/places/${pid}/universe`)
                        .then((res: any) => ({ placeId: pid, universeId: res.data.universeId }))
                        .catch(() => null)
                );

                // Wait for thumbnails and universe IDs
                const [thumbRes, ...universeResults] = await Promise.all([
                    thumbPromise,
                    ...universePromises
                ]);

                // Process Thumbnails
                if (thumbRes && thumbRes.data && thumbRes.data.data) {
                    for (const item of thumbRes.data.data) {
                        const game = games.find(g => g.placeId === item.targetId);
                        if (game) {
                            game.imageUrl = item.imageUrl;
                        }
                    }
                }

                // Process Universe IDs and fetch Game Names
                const validMappings = universeResults.filter((r: any) => r !== null) as { placeId: number, universeId: number }[];
                const universeIds = [...new Set(validMappings.map(m => m.universeId))];

                if (universeIds.length > 0) {
                    try {
                        const gamesRes = await axios.get('https://games.roblox.com/v1/games', {
                            params: { universeIds: universeIds.join(',') }
                        });

                        if (gamesRes.data && gamesRes.data.data) {
                            for (const info of gamesRes.data.data) {
                                const matchingPlaceIds = validMappings
                                    .filter(m => m.universeId === info.id)
                                    .map(m => m.placeId);
                                
                                for (const pid of matchingPlaceIds) {
                                    const game = games.find(g => g.placeId === pid);
                                    if (game) {
                                        game.name = info.name;
                                    }
                                }
                            }
                        }
                    } catch (e) {
                        console.warn('Failed to fetch game names from universe IDs', e);
                    }
                }

            } catch (e) {
                console.error('Failed to fetch game info', e);
            }
        }
        
        // Ensure name is set
        games.forEach(g => {
            if (!g.name) g.name = `Game ${g.placeId}`;
        });
        socket.emit('gamesList', games);
    });

    socket.on('sendMessage', (data: { jobId: string, username: string, content: string, userId?: number }) => {
        const { jobId, username, content, userId } = data;
        if (!jobId || !username || !content) return;

        // Handle DM routing (Negative Job IDs)
        if (jobId.startsWith('-')) {
            const targetUserId = parseInt(jobId.substring(1));
            if (!isNaN(targetUserId)) {
                const senderUserId = (socket as any).userId || userId;
                
                // Construct message
                const message = {
                    jobId, // Will be overridden for each recipient
                    username,
                    userId: senderUserId,
                    content,
                    timestamp: new Date(),
                    avatarUrl: undefined // Could fetch if needed
                };

                // 1. Send to Target
                const targetSocketId = userSockets.get(targetUserId);
                if (targetSocketId) {
                    // For the target, the conversation is with the SENDER.
                    // So the JobId should be -SenderUserId
                    const targetMessage = { ...message, jobId: `-${senderUserId}` };
                    io.to(targetSocketId).emit('receiveMessage', targetMessage);
                }

                // 2. Echo to Sender
                // For the sender, the conversation is with the TARGET.
                // So the JobId should be -TargetUserId (which is the original jobId)
                const senderMessage = { ...message, jobId: `-${targetUserId}` };
                socket.emit('receiveMessage', senderMessage);
                
                return;
            }
        }

        const channel = registry.getChannel(jobId);
        if (!channel) return;

        const participant = channel.getParticipant(username);
        const finalUserId = userId ?? participant?.userId;
        const avatarUrl = participant?.avatarUrl;

        const message = {
            jobId,
            username,
            userId: finalUserId,
            content,
            timestamp: new Date(),
            avatarUrl
        };

        channel.appendMessage(message);
        io.to(jobId).emit('receiveMessage', message);
    });

    socket.on('notifyTyping', (data: { jobId: string, username: string, isTyping: boolean }) => {
        const { jobId, username, isTyping } = data;
        if (!jobId || !username) return;

        registry.setTypingState(jobId, username, isTyping);
        
        io.to(jobId).emit('typingIndicator', {
            jobId,
            usernames: registry.getTypingParticipants(jobId)
        });
    });

    socket.on('sendPrivateMessage', (data: { toUserId: number, content: string, fromUsername: string, fromUserId: number }) => {
        console.log('sendPrivateMessage received:', data);
        const { toUserId, content, fromUsername, fromUserId } = data;
        if (!toUserId || !content || !fromUserId) {
            console.log('Invalid private message data');
            return;
        }

        const targetSocketId = userSockets.get(toUserId);
        console.log(`Target UserId: ${toUserId}, SocketId: ${targetSocketId}`);

        const message = {
            fromUserId,
            fromUsername,
            toUserId,
            content,
            timestamp: new Date()
        };

        // Only send to target if it's not the sender (avoid double echo)
        if (targetSocketId && targetSocketId !== socket.id) {
            io.to(targetSocketId).emit('receivePrivateMessage', message);
        } else if (!targetSocketId) {
            console.log('Target user not found in userSockets');
        }
        
        // Echo back to sender so they know it was sent (and can display it)
        socket.emit('receivePrivateMessage', message);
    });

    socket.on('getGroups', () => {
        const userId = (socket as any).userId;
        if (!userId) return;
        
        const groups = groupRegistry.getUserGroups(userId);
        socket.emit('userGroups', groups);
    });

    socket.on('createGroup', (data: { participants: number[], name?: string }) => {
        const userId = (socket as any).userId;
        if (!userId) return;

        const group = groupRegistry.createGroup(userId, data.participants, data.name);
        
        // Notify all participants
        group.participants.forEach(pId => {
            const sId = userSockets.get(pId);
            if (sId) {
                io.to(sId).emit('groupCreated', group);
            }
        });
    });

    socket.on('sendGroupMessage', (data: { groupId: string, content: string }) => {
        const userId = (socket as any).userId;
        const username = (socket as any).username;
        if (!userId || !username) return;

        const message = groupRegistry.addMessage(data.groupId, userId, username, data.content);
        if (message) {
            const group = groupRegistry.getGroup(data.groupId);
            if (group) {
                group.participants.forEach(pId => {
                    const sId = userSockets.get(pId);
                    if (sId) {
                        io.to(sId).emit('receiveGroupMessage', message);
                    }
                });
            }
        }
    });

    socket.on('disconnect', () => {
        const jobId = (socket as any).jobId;
        const username = (socket as any).username;
        
        // Remove from userSockets if we can find the userId
        // Since we don't store userId on socket explicitly in joinChannel (only in closure), 
        // we might need to iterate or store it. 
        // Optimization: Store userId on socket.
        const userId = (socket as any).userId;
        if (userId) {
            userSockets.delete(userId);
        }

        if (jobId && username) {
            const key = `${jobId}:${username}`;
            if (disconnectTimeouts.has(key)) {
                clearTimeout(disconnectTimeouts.get(key));
            }

            const timeout = setTimeout(() => {
                registry.removeParticipant(jobId, username);
                registry.setTypingState(jobId, username, false);

                io.to(jobId).emit('participantsChanged', {
                    jobId,
                    participants: registry.getParticipants(jobId)
                });

                io.to(jobId).emit('typingIndicator', {
                    jobId,
                    usernames: registry.getTypingParticipants(jobId)
                });
                disconnectTimeouts.delete(key);
            }, 5000);

            disconnectTimeouts.set(key, timeout);
        }
        console.log('User disconnected:', socket.id);
    });
});

const PORT = process.env.PORT || 5158;
server.listen(PORT, () => {
    console.log(`BloxCord Server running on port ${PORT}`);
});
