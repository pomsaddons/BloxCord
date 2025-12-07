import { ChannelRecord, ChannelParticipant } from '../models/ChannelRecord';

export class ChannelRegistry {
    private channels: Map<string, ChannelRecord> = new Map();

    public createOrGetChannel(jobId: string, username: string, userId?: number, avatarUrl?: string, placeId?: number): ChannelRecord {
        let channel = this.channels.get(jobId);
        if (!channel) {
            channel = new ChannelRecord(jobId, username, userId, avatarUrl, placeId);
            this.channels.set(jobId, channel);
        }
        channel.addParticipant(username, userId, avatarUrl);
        return channel;
    }

    public getGames(): any[] {
        const games = new Map<number, any>();

        for (const channel of this.channels.values()) {
            if (!channel.placeId) continue;
            if (channel.getParticipants().length === 0) continue;

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

    public getChannel(jobId: string): ChannelRecord | undefined {
        return this.channels.get(jobId);
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
