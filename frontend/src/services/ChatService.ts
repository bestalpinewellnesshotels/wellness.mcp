/**
 * ChatService - Kommunikation mit dem Backend Chat API
 * 
 * Verantwortlich für:
 * - REST-Kommunikation mit Chat-Endpunkten
 * - Session-Management
 * - Fehlerbehandlung
 */

export interface ChatResponse {
    sessionId?: string;
    success: boolean;
    finalAnswer?: string;
    errorMessage?: string;
    responseType?: string;
    timestamp: string;
}

export class ChatService {
    private apiBase: string = '';
    private hotelId: string = '';

    /**
     * Konfiguriert den Service mit API-Base und Hotel-ID
     */
    configure(apiBase: string, hotelId: string) {
        this.apiBase = apiBase;
        this.hotelId = hotelId;
    }

    /**
     * Sendet eine Chat-Nachricht an das Backend
     */
    async sendMessage(message: string, sessionId: string | null, isVoiceInput: boolean = false): Promise<ChatResponse> {
        const url = `${this.apiBase}/api/chat/recommend`;
        
        const requestBody = {
            requirements: message,
            sessionId: sessionId,
            language: this.detectLanguage(message)
        };

        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(requestBody)
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const data: ChatResponse = await response.json();
            return data;

        } catch (error) {
            console.error('ChatService: Error sending message', error);
            throw error;
        }
    }

    /**
     * Einfache Sprach-Erkennung (kann erweitert werden)
     */
    private detectLanguage(text: string): string {
        // Sehr einfache Heuristik - in Produktion besser im Backend
        const germanWords = ['der', 'die', 'das', 'und', 'ist', 'ein', 'zu', 'in'];
        const lowerText = text.toLowerCase();
        
        const hasGermanWords = germanWords.some(word => lowerText.includes(` ${word} `));
        return hasGermanWords ? 'de' : 'en';
    }
}
