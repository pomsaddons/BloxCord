import express from 'express';
import http from 'http';
import { Server, Socket } from 'socket.io';
import cors from 'cors';
import { ChannelRegistry } from './services/ChannelRegistry';
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
const avatarService = new RobloxAvatarService();
const disconnectTimeouts = new Map<string, any>();

io.on('connection', (socket: Socket) => {
    console.log('A user connected:', socket.id);

    socket.on('joinChannel', async (data: { jobId: string, username: string, userId?: number, placeId?: number }) => {
        const { jobId, username, userId, placeId } = data;
        if (!jobId || !username) return;

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

    socket.on('getGames', async () => {
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

    socket.on('disconnect', () => {
        const jobId = (socket as any).jobId;
        const username = (socket as any).username;

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
