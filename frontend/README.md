# Hotel Chatbot Widget - Frontend

Vanilla TypeScript Web Component für Chatbot-Integration in Hotel-Webseiten.

## Features

- ✅ Web Component mit Shadow DOM (vollständige Isolation)
- ✅ Vanilla TypeScript (kein Framework)
- ✅ Text- und Voice-Input
- ✅ Session Management
- ✅ Responsive Design
- ✅ Light & Dark Theme
- ✅ Ein einziges distributables Bundle

## Development

```bash
# Dependencies installieren
npm install

# Development Build mit Watch
npm run dev

# Production Build
npm run build
```

## Integration

Fügen Sie das Widget zu Ihrer Webseite hinzu:

```html
<script 
  src="https://cdn.example.com/chatbot.js"
  data-hotel-id="your_hotel_id"
  data-api-base="https://api.example.com"
  data-theme="light"
  data-position="bottom-right">
</script>
```

## Konfiguration

- `data-hotel-id`: Eindeutige Hotel-ID (erforderlich)
- `data-api-base`: Backend API URL (erforderlich)
- `data-theme`: `light` oder `dark` (optional, Standard: `light`)
- `data-position`: `bottom-right` oder `bottom-left` (optional, Standard: `bottom-right`)

## Architektur

```
src/
├── chatbot.ts                 # Entry Point
├── components/
│   └── ChatbotWidget.ts       # Haupt-Komponente (Web Component)
├── services/
│   ├── ChatService.ts         # REST API Kommunikation
│   └── VoiceService.ts        # Speech-to-Text
├── types/
│   └── ChatMessage.ts         # Type Definitions
├── templates/
│   └── widget-template.ts     # HTML Template
└── styles/
    └── widget-styles.ts       # CSS Styles (Shadow DOM)
```

## Browser Support

- Chrome/Edge 88+
- Firefox 91+
- Safari 14+

Benötigt:
- Custom Elements v1
- Shadow DOM v1
- MediaDevices API (für Voice Input)
