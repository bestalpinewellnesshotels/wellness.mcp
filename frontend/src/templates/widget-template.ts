/**
 * Widget HTML Template
 * 
 * Vollständig isoliert im Shadow DOM
 */

export function template(): string {
    return `
        <!-- Toggle Button (immer sichtbar) -->
        <button id="toggleButton" class="toggle-button" aria-label="Chat öffnen">
            💬
        </button>

        <!-- Chat Container (wird bei Klick angezeigt) -->
        <div class="chat-container">
            <!-- Header -->
            <div class="chat-header">
                <h3>Hotel Chat</h3>
                <button id="closeButton" class="close-button" aria-label="Chat schließen">
                    ×
                </button>
            </div>

            <!-- Messages Area -->
            <div class="messages-container">
                <div class="message message-assistant">
                    <div class="message-content">
                        Hallo! Wie kann ich Ihnen helfen?
                    </div>
                    <div class="message-time">Jetzt</div>
                </div>
            </div>

            <!-- Input Area -->
            <div class="input-container">
                <button id="voiceButton" class="voice-button" aria-label="Spracheingabe">
                    🎤
                </button>
                <input 
                    type="text" 
                    id="chatInput" 
                    placeholder="Ihre Frage..." 
                    aria-label="Chat Eingabe"
                />
                <button id="sendButton" class="send-button" aria-label="Nachricht senden">
                    ➤
                </button>
            </div>
        </div>
    `;
}
