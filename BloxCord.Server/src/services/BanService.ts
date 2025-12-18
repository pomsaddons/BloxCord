import fs from 'fs';
import path from 'path';

export type BanList = {
    appealUrl?: string;
    bannedUserIds?: number[];
    reasonsByUserId?: Record<string, string>;
};

export class BanService {
    private bans: BanList = {};

    constructor() {
        this.load();
    }

    public load() {
        try {
            const filePath = path.join(__dirname, '..', '..', 'bans.json');
            if (!fs.existsSync(filePath)) {
                this.bans = {};
                return;
            }
            const raw = fs.readFileSync(filePath, 'utf-8');
            this.bans = JSON.parse(raw) as BanList;
        } catch {
            this.bans = {};
        }
    }

    public isBanned(userId?: number): { banned: boolean; reason?: string; appealUrl?: string } {
        if (typeof userId !== 'number') return { banned: false };

        const list = this.bans?.bannedUserIds ?? [];
        if (!list.includes(userId)) return { banned: false };

        const reason = this.bans?.reasonsByUserId?.[String(userId)];
        return { banned: true, reason, appealUrl: this.bans?.appealUrl };
    }
}
