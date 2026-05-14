/**
 * Widget Styles
 * 
 * Vollständig isoliert im Shadow DOM - keine CSS-Konflikte mit Host-Seite
 */

export function styles(theme: string, position: string): string {
    const colors = theme === 'dark' ? {
        bg: '#1a1a1a',
        bgSecondary: '#2d2d2d',
        text: '#ffffff',
        textSecondary: '#b3b3b3',
        primary: '#0066cc',
        userBubble: '#0066cc',
        assistantBubble: '#2d2d2d',
        border: '#444444'
    } : {
        bg: '#ffffff',
        bgSecondary: '#f5f5f5',
        text: '#333333',
        textSecondary: '#666666',
        primary: '#0066cc',
        userBubble: '#0066cc',
        assistantBubble: '#e9ecef',
        border: '#dddddd'
    };

    const positionStyles = position === 'bottom-right' 
        ? 'bottom: 20px; right: 20px;'
        : position === 'bottom-left'
        ? 'bottom: 20px; left: 20px;'
        : 'bottom: 20px; right: 20px;';

    return `
        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        :host {
            --color-bg: ${colors.bg};
            --color-bg-secondary: ${colors.bgSecondary};
            --color-text: ${colors.text};
            --color-text-secondary: ${colors.textSecondary};
            --color-primary: ${colors.primary};
            --color-user-bubble: ${colors.userBubble};
            --color-assistant-bubble: ${colors.assistantBubble};
            --color-border: ${colors.border};
            
            position: fixed;
            ${positionStyles}
            z-index: 9999;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
        }

        /* Toggle Button */
        .toggle-button {
            width: 60px;
            height: 60px;
            border-radius: 50%;
            background: var(--color-primary);
            color: white;
            border: none;
            font-size: 24px;
            cursor: pointer;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
            transition: all 0.3s ease;
            display: flex;
            align-items: center;
            justify-content: center;
        }

        .toggle-button:hover {
            transform: scale(1.1);
            box-shadow: 0 6px 16px rgba(0, 0, 0, 0.2);
        }

        /* Chat Container */
        .chat-container {
            position: absolute;
            bottom: 80px;
            right: 0;
            width: 380px;
            height: 600px;
            max-height: calc(100vh - 120px);
            background: var(--color-bg);
            border-radius: 12px;
            box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
            display: none;
            flex-direction: column;
            overflow: hidden;
            transition: all 0.3s ease;
        }

        .chat-container.open {
            display: flex;
        }

        /* Header */
        .chat-header {
            background: var(--color-primary);
            color: white;
            padding: 16px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-shrink: 0;
        }

        .chat-header h3 {
            font-size: 18px;
            font-weight: 600;
            margin: 0;
        }

        .close-button {
            background: transparent;
            border: none;
            color: white;
            font-size: 28px;
            cursor: pointer;
            padding: 0;
            width: 32px;
            height: 32px;
            line-height: 1;
            border-radius: 4px;
        }

        .close-button:hover {
            background: rgba(255, 255, 255, 0.1);
        }

        /* Messages Container */
        .messages-container {
            flex: 1;
            overflow-y: auto;
            padding: 16px;
            display: flex;
            flex-direction: column;
            gap: 12px;
            background: var(--color-bg-secondary);
        }

        .messages-container::-webkit-scrollbar {
            width: 6px;
        }

        .messages-container::-webkit-scrollbar-thumb {
            background: var(--color-border);
            border-radius: 3px;
        }

        /* Messages */
        .message {
            display: flex;
            flex-direction: column;
            max-width: 75%;
            animation: slideIn 0.3s ease;
        }

        @keyframes slideIn {
            from {
                opacity: 0;
                transform: translateY(10px);
            }
            to {
                opacity: 1;
                transform: translateY(0);
            }
        }

        .message-user {
            align-self: flex-end;
        }

        .message-assistant {
            align-self: flex-start;
        }

        .message-system {
            align-self: center;
            max-width: 90%;
        }

        .message-content {
            padding: 10px 14px;
            border-radius: 12px;
            word-wrap: break-word;
            line-height: 1.5;
            font-size: 14px;
        }

        .message-user .message-content {
            background: var(--color-user-bubble);
            color: white;
            border-bottom-right-radius: 4px;
        }

        .message-assistant .message-content {
            background: var(--color-assistant-bubble);
            color: var(--color-text);
            border-bottom-left-radius: 4px;
        }

        .message-system .message-content {
            background: transparent;
            color: var(--color-text-secondary);
            text-align: center;
            font-size: 12px;
            font-style: italic;
        }

        .message-time {
            font-size: 11px;
            color: var(--color-text-secondary);
            margin-top: 4px;
            padding: 0 4px;
        }

        .message-user .message-time {
            text-align: right;
        }

        /* Input Container */
        .input-container {
            display: flex;
            gap: 8px;
            padding: 16px;
            background: var(--color-bg);
            border-top: 1px solid var(--color-border);
            flex-shrink: 0;
        }

        #chatInput {
            flex: 1;
            padding: 10px 12px;
            border: 1px solid var(--color-border);
            border-radius: 8px;
            font-size: 14px;
            background: var(--color-bg);
            color: var(--color-text);
            outline: none;
            transition: border-color 0.2s;
        }

        #chatInput:focus {
            border-color: var(--color-primary);
        }

        #chatInput::placeholder {
            color: var(--color-text-secondary);
        }

        .voice-button,
        .send-button {
            width: 40px;
            height: 40px;
            border-radius: 8px;
            border: none;
            cursor: pointer;
            font-size: 18px;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: all 0.2s;
        }

        .voice-button {
            background: var(--color-bg-secondary);
            color: var(--color-text);
        }

        .voice-button:hover {
            background: var(--color-border);
        }

        .voice-button.recording {
            background: #dc3545;
            color: white;
            animation: pulse 1s infinite;
        }

        @keyframes pulse {
            0%, 100% {
                opacity: 1;
            }
            50% {
                opacity: 0.7;
            }
        }

        .send-button {
            background: var(--color-primary);
            color: white;
        }

        .send-button:hover {
            background: #0052a3;
        }

        .send-button:disabled,
        .voice-button:disabled {
            opacity: 0.5;
            cursor: not-allowed;
        }

        /* Mobile Responsiveness */
        @media (max-width: 480px) {
            .chat-container {
                width: calc(100vw - 40px);
                height: calc(100vh - 120px);
                bottom: 80px;
                right: 20px;
            }
        }
    `;
}
