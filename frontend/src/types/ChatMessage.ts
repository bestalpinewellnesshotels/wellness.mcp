/**
 * Chat Message Type Definition
 */

export interface ChatMessage {
    role: 'user' | 'assistant' | 'system';
    content: string;
    timestamp: Date;
    confidence?: number;
}
