"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.RobloxAvatarService = void 0;
const axios_1 = __importDefault(require("axios"));
class RobloxAvatarService {
    async tryGetHeadshotUrl(userId) {
        try {
            const response = await axios_1.default.get(RobloxAvatarService.BASE_URL, {
                params: {
                    userIds: userId,
                    size: '48x48',
                    format: 'Png',
                    isCircular: true
                }
            });
            if (response.data && response.data.data && response.data.data.length > 0) {
                return response.data.data[0].imageUrl;
            }
            return null;
        }
        catch (error) {
            console.error('Error fetching avatar:', error);
            return null;
        }
    }
}
exports.RobloxAvatarService = RobloxAvatarService;
RobloxAvatarService.BASE_URL = 'https://thumbnails.roblox.com/v1/users/avatar-headshot';
