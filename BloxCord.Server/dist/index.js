"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
const express_1 = __importDefault(require("express"));
const http_1 = __importDefault(require("http"));
const socket_io_1 = require("socket.io");
const cors_1 = __importDefault(require("cors"));
const ChannelRegistry_1 = require("./services/ChannelRegistry");
const RobloxAvatarService_1 = require("./services/RobloxAvatarService");
const axios_1 = __importDefault(require("axios"));
const app = (0, express_1.default)();
app.use((0, cors_1.default)());
app.use(express_1.default.json());
const server = http_1.default.createServer(app);
const io = new socket_io_1.Server(server, {
    cors: {
        origin: "*",
        methods: ["GET", "POST"]
    }
});
const registry = new ChannelRegistry_1.ChannelRegistry();
const avatarService = new RobloxAvatarService_1.RobloxAvatarService();
io.on('connection', (socket) => {
    console.log('A user connected:', socket.id);
    socket.on('joinChannel', async (data) => {
        const { jobId, username, userId, placeId } = data;
        if (!jobId || !username)
            return;
        let avatarUrl = undefined;
        if (userId) {
            const url = await avatarService.tryGetHeadshotUrl(userId);
            if (url)
                avatarUrl = url;
        }
        const channel = registry.createOrGetChannel(jobId, username, userId, avatarUrl, placeId);
        socket.join(jobId);
        // Store session info on socket
        socket.jobId = jobId;
        socket.username = username;
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
                const thumbPromise = axios_1.default.get('https://thumbnails.roblox.com/v1/places/gameicons', {
                    params: {
                        placeIds: placeIds.join(','),
                        returnPolicy: 'PlaceHolder',
                        size: '150x150',
                        format: 'Png',
                        isCircular: false
                    }
                }).catch(e => null);
                // Start fetching universe IDs
                const universePromises = placeIds.map(pid => axios_1.default.get(`https://apis.roblox.com/universes/v1/places/${pid}/universe`)
                    .then(res => ({ placeId: pid, universeId: res.data.universeId }))
                    .catch(() => null));
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
                const validMappings = universeResults.filter(r => r !== null);
                const universeIds = [...new Set(validMappings.map(m => m.universeId))];
                if (universeIds.length > 0) {
                    try {
                        const gamesRes = await axios_1.default.get('https://games.roblox.com/v1/games', {
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
                    }
                    catch (e) {
                        console.warn('Failed to fetch game names from universe IDs', e);
                    }
                }
            }
            catch (e) {
                console.error('Failed to fetch game info', e);
            }
        }
        // Ensure name is set
        games.forEach(g => {
            if (!g.name)
                g.name = `Game ${g.placeId}`;
        });
        socket.emit('gamesList', games);
    });
    socket.on('sendMessage', (data) => {
        const { jobId, username, content, userId } = data;
        if (!jobId || !username || !content)
            return;
        const channel = registry.getChannel(jobId);
        if (!channel)
            return;
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
    socket.on('notifyTyping', (data) => {
        const { jobId, username, isTyping } = data;
        if (!jobId || !username)
            return;
        registry.setTypingState(jobId, username, isTyping);
        io.to(jobId).emit('typingIndicator', {
            jobId,
            usernames: registry.getTypingParticipants(jobId)
        });
    });
    socket.on('disconnect', () => {
        const jobId = socket.jobId;
        const username = socket.username;
        if (jobId && username) {
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
        }
        console.log('User disconnected:', socket.id);
    });
});
const PORT = process.env.PORT || 5158;
server.listen(PORT, () => {
    console.log(`BloxCord Server running on port ${PORT}`);
});
