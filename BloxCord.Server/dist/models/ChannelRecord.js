"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.ChannelRecord = void 0;
class ChannelRecord {
    constructor(jobId, createdBy, userId, avatarUrl, placeId) {
        this.participants = new Map();
        this.history = [];
        this.typingUsers = new Set();
        this.jobId = jobId;
        this.placeId = placeId;
        this.createdBy = createdBy;
        this.createdAt = new Date();
    }
    addParticipant(username, userId, avatarUrl) {
        this.participants.set(username, {
            username,
            userId,
            avatarUrl,
            isTyping: false
        });
    }
    removeParticipant(username) {
        this.participants.delete(username);
        this.typingUsers.delete(username);
    }
    getParticipant(username) {
        return this.participants.get(username);
    }
    getParticipants() {
        return Array.from(this.participants.values());
    }
    appendMessage(message) {
        this.history.push(message);
        if (this.history.length > 100) {
            this.history.shift();
        }
    }
    getHistory() {
        return [...this.history];
    }
    setTypingState(username, isTyping) {
        const participant = this.participants.get(username);
        if (participant) {
            participant.isTyping = isTyping;
            if (isTyping) {
                this.typingUsers.add(username);
            }
            else {
                this.typingUsers.delete(username);
            }
        }
    }
    getTypingParticipants() {
        return Array.from(this.typingUsers);
    }
}
exports.ChannelRecord = ChannelRecord;
