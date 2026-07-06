/**
 * Appointment Chatbot Widget
 * 
 * Embed on any website with one line:
 * <script src="https://yourdomain.com/widget.js"
 *         data-api-url="https://yourdomain.com/api/chat"
 *         data-accent="#14424A">
 * </script>
 *
 * No framework, no npm, no build step required.
 * Works on any website regardless of tech stack.
 */
(function () {
    const currentScript = document.currentScript;
    const apiUrl = currentScript?.getAttribute('data-api-url') || '/api/chat';
    const accentColor = currentScript?.getAttribute('data-accent') || '#14424A';

    // Conversation history — maintained in memory, sent with every request
    let history = [];

    // ── Styles ────────────────────────────────────────────────────────────
    const style = document.createElement('style');
    style.textContent = `
        #appt-launcher {
            position: fixed; bottom: 24px; right: 24px;
            width: 60px; height: 60px; border-radius: 50%;
            background: ${accentColor}; border: none; cursor: pointer;
            box-shadow: 0 4px 16px rgba(0,0,0,0.2); z-index: 999998;
            display: flex; align-items: center; justify-content: center;
            transition: transform 0.2s ease;
        }
        #appt-launcher:hover { transform: scale(1.06); }

        #appt-panel {
            position: fixed; bottom: 96px; right: 24px;
            width: 360px; max-width: calc(100vw - 32px);
            height: 520px; max-height: calc(100vh - 140px);
            background: #FAFAF7; border-radius: 16px;
            box-shadow: 0 12px 40px rgba(0,0,0,0.22);
            display: none; flex-direction: column;
            overflow: hidden; z-index: 999999;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
        }
        #appt-panel.open { display: flex; }

        #appt-header {
            background: ${accentColor}; color: #FAFAF7;
            padding: 16px 18px; font-size: 15px; font-weight: 600;
            display: flex; justify-content: space-between; align-items: center;
        }
        #appt-header button {
            background: none; border: none; color: #FAFAF7;
            font-size: 20px; cursor: pointer; opacity: 0.85; line-height: 1;
        }
        #appt-header button:hover { opacity: 1; }

        #appt-messages {
            flex: 1; overflow-y: auto; padding: 16px;
            display: flex; flex-direction: column; gap: 10px;
        }

        .appt-msg {
            max-width: 82%; padding: 10px 13px; border-radius: 14px;
            font-size: 14px; line-height: 1.45; white-space: pre-wrap;
        }
        .appt-msg.user {
            align-self: flex-end; background: ${accentColor};
            color: #FAFAF7; border-bottom-right-radius: 4px;
        }
        .appt-msg.assistant {
            align-self: flex-start; background: #EFEBE0;
            color: #24302F; border-bottom-left-radius: 4px;
        }
        .appt-msg.typing {
            align-self: flex-start; background: #EFEBE0;
            color: #7A7A72; font-style: italic;
        }

        #appt-inputrow {
            display: flex; border-top: 1px solid #E7E2D6;
            padding: 10px; gap: 8px;
        }
        #appt-input {
            flex: 1; border: 1px solid #DAD4C4; border-radius: 20px;
            padding: 9px 14px; font-size: 14px; outline: none;
            font-family: inherit;
        }
        #appt-input:focus { border-color: ${accentColor}; }
        #appt-send {
            background: ${accentColor}; color: #FAFAF7; border: none;
            border-radius: 20px; padding: 0 18px; font-size: 14px;
            font-weight: 600; cursor: pointer;
        }
        #appt-send:disabled { opacity: 0.5; cursor: default; }
    `;
    document.head.appendChild(style);

    // ── Chat bubble button ────────────────────────────────────────────────
    const launcher = document.createElement('button');
    launcher.id = 'appt-launcher';
    launcher.setAttribute('aria-label', 'Open appointment chat');
    launcher.innerHTML = `
        <svg width="28" height="28" viewBox="0 0 24 24" fill="none">
            <path d="M4 4h16v12H7l-3 3V4z"
                  stroke="#FAFAF7" stroke-width="1.8" stroke-linejoin="round"/>
            <path d="M8 9h8M8 12h5"
                  stroke="#FAFAF7" stroke-width="1.8" stroke-linecap="round"/>
        </svg>`;

    // ── Chat panel ────────────────────────────────────────────────────────
    const panel = document.createElement('div');
    panel.id = 'appt-panel';
    panel.innerHTML = `
        <div id="appt-header">
            <span>Book an appointment</span>
            <button id="appt-close" aria-label="Close">&times;</button>
        </div>
        <div id="appt-messages"></div>
        <div id="appt-inputrow">
            <input id="appt-input" type="text"
                   placeholder="Type a message..." autocomplete="off" />
            <button id="appt-send">Send</button>
        </div>`;

    document.body.appendChild(launcher);
    document.body.appendChild(panel);

    const messagesEl = document.getElementById('appt-messages');
    const inputEl = document.getElementById('appt-input');
    const sendBtn = document.getElementById('appt-send');
    const closeBtn = document.getElementById('appt-close');

    // ── Helpers ───────────────────────────────────────────────────────────
    function addMessage(role, content) {
        const div = document.createElement('div');
        div.className = `appt-msg ${role}`;
        div.textContent = content;
        messagesEl.appendChild(div);
        messagesEl.scrollTop = messagesEl.scrollHeight;
        return div;
    }

    function greet() {
        if (messagesEl.children.length === 0)
            addMessage('assistant',
                "Hi! I can help you book, check, or reschedule an appointment. " +
                "What service are you looking for?");
    }

    // ── Events ────────────────────────────────────────────────────────────
    launcher.addEventListener('click', () => {
        panel.classList.toggle('open');
        if (panel.classList.contains('open')) {
            greet();
            inputEl.focus();
        }
    });

    closeBtn.addEventListener('click', () =>
        panel.classList.remove('open'));

    inputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') sendMessage();
    });

    sendBtn.addEventListener('click', sendMessage);

    // ── Core send function ────────────────────────────────────────────────
    async function sendMessage() {
        const text = inputEl.value.trim();
        if (!text) return;

        addMessage('user', text);
        inputEl.value = '';
        sendBtn.disabled = true;

        // Show typing indicator while waiting for response
        const typingEl = addMessage('typing', 'Typing...');

        try {
            const res = await fetch(apiUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ message: text, history })
            });

            if (!res.ok) throw new Error(`HTTP ${res.status}`);

            const data = await res.json();

            typingEl.remove();
            addMessage('assistant', data.reply);

            // Update history for next request — maintains conversation context
            history = data.history || history;

        } catch (err) {
            typingEl.remove();
            addMessage('assistant',
                "Sorry, something went wrong. Please try again in a moment.");
            console.error('Appointment chat error:', err);
        } finally {
            sendBtn.disabled = false;
            inputEl.focus();
        }
    }
})();