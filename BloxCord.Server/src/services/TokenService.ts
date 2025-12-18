import fs from 'fs';
import path from 'path';
import crypto from 'crypto';

type TokenStore = {
    tokensByUserId?: Record<string, string>;
};

export class TokenService {
    private store: TokenStore = {};

    constructor() {
        this.load();
    }

    private getFilePath(): string {
        return path.join(__dirname, '..', '..', 'tokens.json');
    }

    private load() {
        try {
            const filePath = this.getFilePath();
            if (!fs.existsSync(filePath)) {
                this.store = { tokensByUserId: {} };
                return;
            }
            const raw = fs.readFileSync(filePath, 'utf-8');
            this.store = (JSON.parse(raw) as TokenStore) ?? { tokensByUserId: {} };
            if (!this.store.tokensByUserId) this.store.tokensByUserId = {};
        } catch {
            this.store = { tokensByUserId: {} };
        }
    }

    private save() {
        try {
            const filePath = this.getFilePath();
            fs.writeFileSync(filePath, JSON.stringify(this.store, null, 2), 'utf-8');
        } catch {
            // Ignore persistence failures
        }
    }

    public getToken(userId: number): string | undefined {
        return this.store.tokensByUserId?.[String(userId)];
    }

    public isTokenValid(userId: number, token: string): boolean {
        const expected = this.getToken(userId);
        if (!expected) return false;
        return expected === token;
    }

    public getOrCreateToken(userId: number): string {
        const existing = this.getToken(userId);
        if (existing) return existing;

        const token = crypto.randomBytes(24).toString('base64url');
        if (!this.store.tokensByUserId) this.store.tokensByUserId = {};
        this.store.tokensByUserId[String(userId)] = token;
        this.save();
        return token;
    }
}
