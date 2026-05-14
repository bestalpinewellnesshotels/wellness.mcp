/**
 * Hotel Chatbot Widget - Web Component
 * 
 * Vanilla TypeScript Web Component mit Shadow DOM für vollständige Isolation.
 * Einbindung via: <script src="chatbot.js" data-hotel-id="..." data-api-base="...">
 */

import { ChatbotWidget } from './components/ChatbotWidget';

// Auto-Initialisierung wenn script geladen wird
(() => {
    // Web Component registrieren
    if (!customElements.get('hotel-chatbot')) {
        customElements.define('hotel-chatbot', ChatbotWidget);
    }

    // Auto-init basierend auf script tag data-* Attributen
    const initWidget = () => {
        const scripts = document.querySelectorAll('script[data-hotel-id]');
        scripts.forEach(script => {
            const hotelId = script.getAttribute('data-hotel-id');
            const apiBase = script.getAttribute('data-api-base') || 'https://api.example.com';
            const theme = script.getAttribute('data-theme') || 'light';
            const position = script.getAttribute('data-position') || 'bottom-right';

            if (hotelId && !document.querySelector('hotel-chatbot')) {
                const widget = document.createElement('hotel-chatbot') as ChatbotWidget;
                widget.setAttribute('hotel-id', hotelId);
                widget.setAttribute('api-base', apiBase);
                widget.setAttribute('theme', theme);
                widget.setAttribute('position', position);
                document.body.appendChild(widget);
            }
        });
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initWidget);
    } else {
        initWidget();
    }
})();
