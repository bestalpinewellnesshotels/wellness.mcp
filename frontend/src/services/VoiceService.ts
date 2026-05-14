/**
 * VoiceService - Speech-to-Text via Backend API
 * 
 * Verantwortlich für:
 * - Audio-Aufnahme via MediaDevices API
 * - Audio-Encoding
 * - Upload an Backend Speech API
 */

export class VoiceService {
    private apiBase: string = '';
    private hotelId: string = '';
    private mediaRecorder: MediaRecorder | null = null;
    private audioChunks: Blob[] = [];

    /**
     * Konfiguriert den Service
     */
    configure(apiBase: string, hotelId: string) {
        this.apiBase = apiBase;
        this.hotelId = hotelId;
    }

    /**
     * Startet Audio-Aufnahme
     */
    async startRecording(): Promise<void> {
        try {
            // Mikrofon-Zugriff anfordern
            const stream = await navigator.mediaDevices.getUserMedia({ 
                audio: {
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                } 
            });

            // MediaRecorder erstellen
            this.mediaRecorder = new MediaRecorder(stream, {
                mimeType: this.getSupportedMimeType()
            });

            this.audioChunks = [];

            this.mediaRecorder.ondataavailable = (event) => {
                if (event.data.size > 0) {
                    this.audioChunks.push(event.data);
                }
            };

            this.mediaRecorder.start();

        } catch (error) {
            console.error('VoiceService: Error starting recording', error);
            throw new Error('Mikrofon-Zugriff verweigert oder nicht verfügbar');
        }
    }

    /**
     * Stoppt Audio-Aufnahme und sendet an Backend
     */
    async stopRecording(sessionId: string | null): Promise<string> {
        return new Promise((resolve, reject) => {
            if (!this.mediaRecorder) {
                reject(new Error('No active recording'));
                return;
            }

            this.mediaRecorder.onstop = async () => {
                try {
                    // Audio-Blob erstellen
                    const audioBlob = new Blob(this.audioChunks, { 
                        type: this.mediaRecorder!.mimeType 
                    });

                    // Stream stoppen
                    this.mediaRecorder!.stream.getTracks().forEach(track => track.stop());

                    // An Backend senden
                    const text = await this.transcribe(audioBlob, sessionId);
                    resolve(text);

                } catch (error) {
                    reject(error);
                }
            };

            this.mediaRecorder.stop();
        });
    }

    /**
     * Sendet Audio an Backend für Transkription
     */
    private async transcribe(audioBlob: Blob, sessionId: string | null): Promise<string> {
        const url = `${this.apiBase}/api/voice/transcribe`;

        // Audio zu Base64 konvertieren
        const audioBase64 = await this.blobToBase64(audioBlob);

        const requestBody = {
            hotelId: this.hotelId,
            sessionId: sessionId,
            audioDataBase64: audioBase64.split(',')[1], // Remove data:audio/...;base64, prefix
            audioFormat: audioBlob.type
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

            const data = await response.json();
            return data.text || '';

        } catch (error) {
            console.error('VoiceService: Error transcribing audio', error);
            throw error;
        }
    }

    /**
     * Konvertiert Blob zu Base64
     */
    private blobToBase64(blob: Blob): Promise<string> {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onloadend = () => resolve(reader.result as string);
            reader.onerror = reject;
            reader.readAsDataURL(blob);
        });
    }

    /**
     * Ermittelt unterstützten MIME-Type für MediaRecorder
     */
    private getSupportedMimeType(): string {
        const types = [
            'audio/webm;codecs=opus',
            'audio/webm',
            'audio/ogg;codecs=opus',
            'audio/wav'
        ];

        for (const type of types) {
            if (MediaRecorder.isTypeSupported(type)) {
                return type;
            }
        }

        return ''; // Browser default
    }
}
