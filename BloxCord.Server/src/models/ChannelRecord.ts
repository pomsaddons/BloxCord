export interface ChatMessage {
    /** Stable unique message id (UUID). */
    id: string;
    jobId: string;
    username: string;
    userId?: number;
    content: string;
    timestamp: Date;
    avatarUrl?: string;

    /** Message this is replying to (by id). */
    replyToId?: string;

    /** Server-side edits/deletes (for future use). */
    editedAt?: Date;
    deletedAt?: Date;

    /** Optional system message marker (for future use). */
    isSystem?: boolean;

    /** Reactions keyed by emoji. */
    reactions?: Record<string, { usernames: string[]; userIds: number[] }>;
}

export interface ChannelParticipant {
    username: string;
    userId?: number;
    avatarUrl?: string;
    isTyping: boolean;

    countryCode?: string;
    preferredLanguage?: string;
    dmPublicKey?: string;
}

export class ChannelRecord {
    public jobId: string;
    public placeId?: number;
    public createdBy: string;
    public createdAt: Date;
    private participants: Map<string, ChannelParticipant> = new Map();
    private history: ChatMessage[] = [];
    private historyById: Map<string, ChatMessage> = new Map();
    private authorTokenByMessageId: Map<string, string> = new Map();
    private typingUsers: Set<string> = new Set();

    private languageCode: string = 'en';
    private languageVotesByCode: Map<string, Set<string>> = new Map();
    private languageVoteByVoter: Map<string, string> = new Map();

    private pinnedMessageId: string | null = null;
    private activePinVote: { messageId: string; voters: Set<string> } | null = null;
    private activeKickVote: { targetUsername: string; voters: Set<string> } | null = null;

    private static readonly MaxHistory = 100;

    constructor(jobId: string, createdBy: string, userId?: number, avatarUrl?: string, placeId?: number) {
        this.jobId = jobId;
        this.placeId = placeId;
        this.createdBy = createdBy;
        this.createdAt = new Date();
    }

    public addParticipant(username: string, userId?: number, avatarUrl?: string, meta?: { countryCode?: string; preferredLanguage?: string; dmPublicKey?: string }) {
        this.participants.set(username, {
            username,
            userId,
            avatarUrl,
            isTyping: false,
            countryCode: meta?.countryCode,
            preferredLanguage: meta?.preferredLanguage,
            dmPublicKey: meta?.dmPublicKey
        });
    }

    public removeParticipant(username: string) {
        this.participants.delete(username);
        this.typingUsers.delete(username);
    }

    public getParticipant(username: string): ChannelParticipant | undefined {
        return this.participants.get(username);
    }

    public updateParticipantMeta(username: string, meta: { countryCode?: string; preferredLanguage?: string; dmPublicKey?: string }) {
        const participant = this.participants.get(username);
        if (!participant) return;

        if (typeof meta.countryCode === 'string') participant.countryCode = meta.countryCode;
        if (typeof meta.preferredLanguage === 'string') participant.preferredLanguage = meta.preferredLanguage;
        if (typeof meta.dmPublicKey === 'string') participant.dmPublicKey = meta.dmPublicKey;
    }

    public getParticipants(): ChannelParticipant[] {
        return Array.from(this.participants.values());
    }

    public appendMessage(message: ChatMessage, authorToken?: string) {
        this.history.push(message);
        this.historyById.set(message.id, message);
        if (typeof authorToken === 'string' && authorToken.length > 0) {
            this.authorTokenByMessageId.set(message.id, authorToken);
        }

        while (this.history.length > ChannelRecord.MaxHistory) {
            const removed = this.history.shift();
            if (removed) {
                this.historyById.delete(removed.id);
                this.authorTokenByMessageId.delete(removed.id);
            }
        }
    }

    public getAuthorToken(messageId: string): string | undefined {
        return this.authorTokenByMessageId.get(messageId);
    }

    public getMessageById(id: string): ChatMessage | undefined {
        return this.historyById.get(id);
    }

    public tryUpdateMessage(messageId: string, patch: { content?: string; editedAt?: Date; deletedAt?: Date }): ChatMessage | undefined {
        const existing = this.historyById.get(messageId);
        if (!existing) return undefined;

        const updated: ChatMessage = {
            ...existing,
            content: patch.content ?? existing.content,
            editedAt: patch.editedAt ?? existing.editedAt,
            deletedAt: patch.deletedAt ?? existing.deletedAt
        };

        // Update both index + ordered history (preserve order).
        this.historyById.set(messageId, updated);
        const idx = this.history.findIndex(m => m.id === messageId);
        if (idx >= 0) {
            this.history[idx] = updated;
        }

        return updated;
    }

    public tryAddReaction(messageId: string, emoji: string, actor: { username: string; userId?: number }): ChatMessage | undefined {
        const existing = this.historyById.get(messageId);
        if (!existing) return undefined;

        const reactions = { ...(existing.reactions ?? {}) };
        const bucket = reactions[emoji] ?? { usernames: [], userIds: [] };

        if (!bucket.usernames.includes(actor.username)) {
            bucket.usernames = [...bucket.usernames, actor.username];
        }
        if (typeof actor.userId === 'number' && !bucket.userIds.includes(actor.userId)) {
            bucket.userIds = [...bucket.userIds, actor.userId];
        }

        reactions[emoji] = bucket;
        return this.replaceMessage(messageId, { ...existing, reactions });
    }

    public tryRemoveReaction(messageId: string, emoji: string, actor: { username: string; userId?: number }): ChatMessage | undefined {
        const existing = this.historyById.get(messageId);
        if (!existing?.reactions?.[emoji]) return undefined;

        const reactions = { ...existing.reactions };
        const bucket = reactions[emoji];

        const nextUsernames = bucket.usernames.filter(u => u !== actor.username);
        const nextUserIds = typeof actor.userId === 'number'
            ? bucket.userIds.filter(id => id !== actor.userId)
            : bucket.userIds;

        if (nextUsernames.length === 0 && nextUserIds.length === 0) {
            delete reactions[emoji];
        } else {
            reactions[emoji] = { usernames: nextUsernames, userIds: nextUserIds };
        }

        return this.replaceMessage(messageId, { ...existing, reactions });
    }

    private replaceMessage(messageId: string, message: ChatMessage): ChatMessage {
        this.historyById.set(messageId, message);
        const idx = this.history.findIndex(m => m.id === messageId);
        if (idx >= 0) {
            this.history[idx] = message;
        }
        return message;
    }

    public getHistory(): ChatMessage[] {
        return [...this.history];
    }

    public getPinnedMessageId(): string | null {
        return this.pinnedMessageId;
    }

    public getActivePinVote(): { messageId: string; voters: string[] } | null {
        if (!this.activePinVote) return null;
        return { messageId: this.activePinVote.messageId, voters: Array.from(this.activePinVote.voters) };
    }

    public getActiveKickVote(): { targetUsername: string; voters: string[] } | null {
        if (!this.activeKickVote) return null;
        return { targetUsername: this.activeKickVote.targetUsername, voters: Array.from(this.activeKickVote.voters) };
    }

    public votePin(messageId: string, username: string): { pinnedMessageId: string | null; vote: { messageId: string; voters: string[] } | null; pinnedNow: boolean } {
        if (!this.activePinVote || this.activePinVote.messageId !== messageId) {
            this.activePinVote = { messageId, voters: new Set() };
        }
        this.activePinVote.voters.add(username);

        const participantCount = this.getParticipants().length;
        const needed = Math.floor(participantCount / 2) + 1;
        const pinnedNow = this.activePinVote.voters.size >= needed;
        if (pinnedNow) {
            this.pinnedMessageId = messageId;
            this.activePinVote = null;
        }
        return { pinnedMessageId: this.pinnedMessageId, vote: this.getActivePinVote(), pinnedNow };
    }

    public voteKick(targetUsername: string, voterUsername: string): { kickedNow: boolean; vote: { targetUsername: string; voters: string[] } | null } {
        if (!this.activeKickVote || !targetUsername || this.activeKickVote.targetUsername !== targetUsername) {
            this.activeKickVote = { targetUsername, voters: new Set() };
        }
        this.activeKickVote.voters.add(voterUsername);

        const participantCount = this.getParticipants().length;
        const needed = Math.floor(participantCount / 2) + 1;
        const kickedNow = this.activeKickVote.voters.size >= needed;
        if (kickedNow) {
            this.activeKickVote = null;
        }
        return { kickedNow, vote: this.getActiveKickVote() };
    }

    public setTypingState(username: string, isTyping: boolean) {
        const participant = this.participants.get(username);
        if (participant) {
            participant.isTyping = isTyping;
            if (isTyping) {
                this.typingUsers.add(username);
            } else {
                this.typingUsers.delete(username);
            }
        }
    }

    public getTypingParticipants(): string[] {
        return Array.from(this.typingUsers);
    }

    public getLanguageCode(): string {
        return this.languageCode;
    }

    public voteLanguage(code: string, username: string): { languageCode: string; changedNow: boolean; votes: Record<string, string[]> } {
        const normalized = (code || '').trim().toLowerCase();
        if (!normalized) {
            return { languageCode: this.languageCode, changedNow: false, votes: this.getLanguageVotes() };
        }

        // Remove previous vote
        const prev = this.languageVoteByVoter.get(username);
        if (prev) {
            const set = this.languageVotesByCode.get(prev);
            if (set) {
                set.delete(username);
                if (set.size === 0) this.languageVotesByCode.delete(prev);
            }
        }

        this.languageVoteByVoter.set(username, normalized);
        const bucket = this.languageVotesByCode.get(normalized) ?? new Set<string>();
        bucket.add(username);
        this.languageVotesByCode.set(normalized, bucket);

        const participantCount = this.getParticipants().length;
        const needed = Math.floor(participantCount / 2) + 1;

        let changedNow = false;
        for (const [lang, voters] of this.languageVotesByCode.entries()) {
            if (voters.size >= needed) {
                this.languageCode = lang;
                this.languageVotesByCode.clear();
                this.languageVoteByVoter.clear();
                changedNow = true;
                break;
            }
        }

        return { languageCode: this.languageCode, changedNow, votes: this.getLanguageVotes() };
    }

    private getLanguageVotes(): Record<string, string[]> {
        const out: Record<string, string[]> = {};
        for (const [lang, voters] of this.languageVotesByCode.entries()) {
            out[lang] = Array.from(voters);
        }
        return out;
    }
}
