/**
 * ChatbotWidget - Hauptkomponente als Web Component
 * 
 * Features:
 * - Shadow DOM für vollständige Isolation
 * - Text & Voice Input
 * - Session Management
 * - Keine globalen Variablen oder CSS-Verschmutzung
 */

import { ChatService } from '../services/ChatService';
import { VoiceService } from '../services/VoiceService';
import { ChatMessage } from '../types/ChatMessage';
import { styles } from '../styles/widget-styles';
import { template } from '../templates/widget-template';

export class ChatbotWidget extends HTMLElement {
    private shadow: ShadowRoot;
    private chatService: ChatService;
    private voiceService: VoiceService;
    
    private hotelId: string = '';
    private apiBase: string = '';
    private sessionId: string | null = null;
    private messages: ChatMessage[] = [];
    private isOpen: boolean = false;
    private isRecording: boolean = false;
    private isSending: boolean = false;

    // DOM-Referenzen
    private chatContainer!: HTMLElement;
    private messagesContainer!: HTMLElement;
    private inputField!: HTMLInputElement;
    private sendButton!: HTMLButtonElement;
    private voiceButton!: HTMLButtonElement;
    private toggleButton!: HTMLButtonElement;

    constructor() {
        super();
        
        // Shadow DOM erstellen (für vollständige Isolation)
        this.shadow = this.attachShadow({ mode: 'open' });
        
        // Services initialisieren
        this.chatService = new ChatService();
        this.voiceService = new VoiceService();
    }

    /**
     * Lifecycle: Component wurde in DOM eingefügt
     */
    connectedCallback() {
        // Attribute lesen
        this.hotelId = this.getAttribute('hotel-id') || '';
        this.apiBase = this.getAttribute('api-base') || '';
        const theme = this.getAttribute('theme') || 'light';
        const position = this.getAttribute('position') || 'bottom-right';

        if (!this.hotelId || !this.apiBase) {
            console.error('hotel-chatbot: hotel-id and api-base attributes are required');
            return;
        }

        // Services konfigurieren
        this.chatService.configure(this.apiBase, this.hotelId);
        this.voiceService.configure(this.apiBase, this.hotelId);

        // UI rendern
        this.render(theme, position);
        
        // Event Listeners
        this.attachEventListeners();
        
        // Session aus localStorage laden (falls vorhanden)
        this.loadSession();
    }

    /**
     * Lifecycle: Component wurde aus DOM entfernt
     */
    disconnectedCallback() {
        this.saveSession();
    }

    /**
     * Rendert das Widget in den Shadow DOM
     */
    private render(theme: string, position: string) {
        // Styles in Shadow DOM injizieren
        const styleSheet = document.createElement('style');
        styleSheet.textContent = styles(theme, position);
        this.shadow.appendChild(styleSheet);

        // Template in Shadow DOM injizieren
        const container = document.createElement('div');
        container.innerHTML = template();
        this.shadow.appendChild(container);

        // DOM-Referenzen cachen
        this.chatContainer = this.shadow.querySelector('.chat-container')!;
        this.messagesContainer = this.shadow.querySelector('.messages-container')!;
        this.inputField = this.shadow.querySelector('#chatInput')!;
        this.sendButton = this.shadow.querySelector('#sendButton')!;
        this.voiceButton = this.shadow.querySelector('#voiceButton')!;
        this.toggleButton = this.shadow.querySelector('#toggleButton')!;
    }

    /**
     * Bindet Event Listeners
     */
    private attachEventListeners() {
        // Toggle-Button (öffnen)
        this.toggleButton.addEventListener('click', () => this.toggleChat());

        // Close-Button im Header
        const closeButton = this.shadow.querySelector('#closeButton') as HTMLButtonElement | null;
        if (closeButton) {
            closeButton.addEventListener('click', (e) => {
                e.stopPropagation();
                this.closeChat();
            });
        }

        // Send-Button
        this.sendButton.addEventListener('click', () => this.sendMessage());

        // Enter-Taste im Input (keydown zum Senden, keyup blockieren damit kein Fokussprung)
        this.inputField.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                this.sendMessage();
            }
        });
        this.inputField.addEventListener('keyup', (e) => {
            if (e.key === 'Enter') e.preventDefault();
        });

        // Voice-Button
        this.voiceButton.addEventListener('click', () => this.toggleVoiceRecording());
    }

    /**
     * Öffnet/Schließt das Chat-Fenster
     */
    private toggleChat() {
        this.isOpen = !this.isOpen;
        if (this.isOpen) {
            this.chatContainer.classList.add('open');
            this.inputField.focus();
        } else {
            this.chatContainer.classList.remove('open');
        }
    }

    private closeChat() {
        this.isOpen = false;
        this.chatContainer.classList.remove('open');
    }

    /**
     * Sendet eine Text-Nachricht
     */
    private async sendMessage() {
        const text = this.inputField.value.trim();
        if (!text || this.isSending) return;

        this.isSending = true;
        this.inputField.value = '';
        this.sendButton.disabled = true;

        // User-Nachricht anzeigen
        this.addMessage({
            role: 'user',
            content: text,
            timestamp: new Date()
        });

        try {
            // An API senden
            const response = await this.chatService.sendMessage(text, this.sessionId);
            
            // Session-ID speichern
            if (response.sessionId) {
                this.sessionId = response.sessionId;
            }

            // Bot-Antwort anzeigen
            const answerText = response.finalAnswer || response.errorMessage || 'Keine Antwort erhalten.';
            this.addMessage({
                role: 'assistant',
                content: answerText,
                timestamp: new Date(response.timestamp)
            });

            // Fehler anzeigen
            if (!response.success && response.errorMessage) {
                this.addMessage({
                    role: 'system',
                    content: response.errorMessage,
                    timestamp: new Date()
                });
            }

        } catch (error) {
            console.error('Error sending message:', error);
            this.addMessage({
                role: 'system',
                content: 'Entschuldigung, es ist ein Fehler aufgetreten. Bitte versuchen Sie es erneut.',
                timestamp: new Date()
            });
        } finally {
            this.isSending = false;
            this.sendButton.disabled = false;
            this.inputField.focus();
        }
    }

    /**
     * Startet/Stoppt Voice-Recording
     */
    private async toggleVoiceRecording() {
        if (!this.isRecording) {
            try {
                this.isRecording = true;
                this.voiceButton.classList.add('recording');
                this.voiceButton.textContent = '⏹️';

                await this.voiceService.startRecording();

            } catch (error) {
                console.error('Error starting recording:', error);
                this.isRecording = false;
                this.voiceButton.classList.remove('recording');
                this.voiceButton.textContent = '🎤';
            }
        } else {
            try {
                this.voiceButton.disabled = true;
                const text = await this.voiceService.stopRecording(this.sessionId);

                if (text) {
                    this.inputField.value = text;
                    // Automatisch senden
                    await this.sendMessage();
                }

            } catch (error) {
                console.error('Error stopping recording:', error);
            } finally {
                this.isRecording = false;
                this.voiceButton.classList.remove('recording');
                this.voiceButton.textContent = '🎤';
                this.voiceButton.disabled = false;
            }
        }
    }

    /**
     * Fügt eine Nachricht zur UI hinzu
     */
    private addMessage(message: ChatMessage) {
        this.messages.push(message);

        const messageEl = document.createElement('div');
        messageEl.className = `message message-${message.role}`;

        const contentEl = document.createElement('div');
        contentEl.className = 'message-content';
        contentEl.textContent = message.content;

        const timeEl = document.createElement('div');
        timeEl.className = 'message-time';
        timeEl.textContent = this.formatTime(message.timestamp);

        messageEl.appendChild(contentEl);
        messageEl.appendChild(timeEl);

        this.messagesContainer.appendChild(messageEl);

        // Auto-scroll nach unten
        this.messagesContainer.scrollTop = this.messagesContainer.scrollHeight;

        // Session speichern
        this.saveSession();
    }

    /**
     * Formatiert Timestamp für Anzeige
     */
    private formatTime(date: Date): string {
        return date.toLocaleTimeString('de-DE', { 
            hour: '2-digit', 
            minute: '2-digit' 
        });
    }

    /**
     * Lädt Session aus localStorage
     */
    private loadSession() {
        const key = `chatbot_session_${this.hotelId}`;
        const stored = localStorage.getItem(key);
        
        if (stored) {
            try {
                const data = JSON.parse(stored);
                this.sessionId = data.sessionId;
                // Keine gespeicherten Nachrichten rendern — jede Seitenladung startet frisch
            } catch (error) {
                console.error('Error loading session:', error);
            }
        }
    }

    /**
     * Speichert Session in localStorage
     */
    private saveSession() {
        const key = `chatbot_session_${this.hotelId}`;
        const data = {
            sessionId: this.sessionId,
            messages: this.messages
        };
        localStorage.setItem(key, JSON.stringify(data));
    }

    /**
     * Spielt Antwort als Audio ab (Text-to-Speech)
     */
    private async playResponseAudio(text: string) {
        try {
            const url = `${this.apiBase}/api/voice/synthesize`;
            
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    text: text,
                    language: 'de-DE'
                })
            });

            if (response.ok) {
                const audioBlob = await response.blob();
                const audioUrl = URL.createObjectURL(audioBlob);
                const audio = new Audio(audioUrl);
                
                audio.onended = () => {
                    URL.revokeObjectURL(audioUrl);
                };
                
                await audio.play();
            }
        } catch (error) {
            console.error('Error playing audio:', error);
            // Fehler nicht anzeigen - TTS ist optional
        }
    }
}
