import { ChannelRecord, ChannelParticipant } from '../models/ChannelRecord';

export class ChannelRegistry {
    private channels: Map<string, ChannelRecord> = new Map();

    public createOrGetChannel(jobId: string, username: string, userId?: number, avatarUrl?: string, placeId?: number, meta?: { countryCode?: string; preferredLanguage?: string; dmPublicKey?: string }): ChannelRecord {
        let channel = this.channels.get(jobId);
        if (!channel) {
            channel = new ChannelRecord(jobId, username, userId, avatarUrl, placeId);
            this.channels.set(jobId, channel);
        }
        channel.addParticipant(username, userId, avatarUrl, meta);
        return channel;
    }

    public getGames(): any[] {
        const games = new Map<number, any>();

        for (const channel of this.channels.values()) {
            if (!channel.placeId) continue;
            if (channel.getParticipants().length === 0) continue;
            if (channel.jobId && channel.jobId.startsWith('-')) continue;

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

        const result = Array.from(games.values()).sort((a, b) => b.serverCount - a.serverCount);
        console.log(`[ChannelRegistry] getGames returning ${result.length} games from ${this.channels.size} channels`);
        return result;
    }

    public getChannel(jobId: string): ChannelRecord | undefined {
        return this.channels.get(jobId);
    }

    public searchUsers(query: string, jobId?: string): ChannelParticipant[] {
        const lowerQuery = query.toLowerCase();
        const results = new Map<string, ChannelParticipant>();

        if (jobId) {
            const channel = this.channels.get(jobId);
            if (channel) {
                for (const p of channel.getParticipants()) {
                    if (p.username.toLowerCase().includes(lowerQuery)) {
                        results.set(p.username, p);
                    }
                }
            }
        } else {
            for (const channel of this.channels.values()) {
                for (const p of channel.getParticipants()) {
                    if (p.username.toLowerCase().includes(lowerQuery)) {
                        if (!results.has(p.username)) {
                            results.set(p.username, p);
                        }
                    }
                }
            }
        }

        return Array.from(results.values()).slice(0, 10);
    }

    public removeParticipant(jobId: string, username: string) {
        const channel = this.channels.get(jobId);
        if (channel) {
            channel.removeParticipant(username);
            if (channel.getParticipants().length === 0) {
                this.channels.delete(jobId);
            }
        }
    }

    public getParticipants(jobId: string): ChannelParticipant[] {
        const channel = this.channels.get(jobId);
        return channel ? channel.getParticipants() : [];
    }

    public getTypingParticipants(jobId: string): string[] {
        const channel = this.channels.get(jobId);
        return channel ? channel.getTypingParticipants() : [];
    }

    public setTypingState(jobId: string, username: string, isTyping: boolean) {
        const channel = this.channels.get(jobId);
        if (channel) {
            channel.setTypingState(username, isTyping);
        }
    }
}
