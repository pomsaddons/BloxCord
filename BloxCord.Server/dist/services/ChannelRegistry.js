"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.ChannelRegistry = void 0;
const ChannelRecord_1 = require("../models/ChannelRecord");
class ChannelRegistry {
    constructor() {
        this.channels = new Map();
    }
    createOrGetChannel(jobId, username, userId, avatarUrl, placeId) {
        let channel = this.channels.get(jobId);
        if (!channel) {
            channel = new ChannelRecord_1.ChannelRecord(jobId, username, userId, avatarUrl, placeId);
            this.channels.set(jobId, channel);
        }
        channel.addParticipant(username, userId, avatarUrl);
        return channel;
    }
    getGames() {
        const games = new Map();
        for (const channel of this.channels.values()) {
            if (!channel.placeId)
                continue;
            if (!games.has(channel.placeId)) {
                games.set(channel.placeId, {
                    placeId: channel.placeId,
                    serverCount: 0,
                    playerCount: 0,
                    servers: []
                });
            }
            const game = games.get(channel.placeId);
            game.serverCount++;
            game.playerCount += channel.getParticipants().length;
            game.servers.push({
                jobId: channel.jobId,
                playerCount: channel.getParticipants().length,
                avatarUrls: channel.getParticipants()
                    .map(p => p.avatarUrl)
                    .filter(url => url)
                    .slice(0, 4)
            });
        }
        return Array.from(games.values()).sort((a, b) => b.serverCount - a.serverCount);
    }
    getChannel(jobId) {
        return this.channels.get(jobId);
    }
    removeParticipant(jobId, username) {
        const channel = this.channels.get(jobId);
        if (channel) {
            channel.removeParticipant(username);
        }
    }
    getParticipants(jobId) {
        const channel = this.channels.get(jobId);
        return channel ? channel.getParticipants() : [];
    }
    getTypingParticipants(jobId) {
        const channel = this.channels.get(jobId);
        return channel ? channel.getTypingParticipants() : [];
    }
    setTypingState(jobId, username, isTyping) {
        const channel = this.channels.get(jobId);
        if (channel) {
            channel.setTypingState(username, isTyping);
        }
    }
}
exports.ChannelRegistry = ChannelRegistry;
