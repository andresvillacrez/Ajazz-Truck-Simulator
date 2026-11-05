let websocket = null;
let context = null;
function el(id) { return document.getElementById(id); }
function connectElgatoStreamDeckSocket(inPort, inUUID, inRegisterEvent, inInfo) {
    context = inUUID;
    websocket = new WebSocket("ws://127.0.0.1:" + inPort);
    websocket.onopen = function() {
        websocket.send(JSON.stringify({ event: inRegisterEvent, uuid: context }));
    };
    websocket.onmessage = function(evt) {
        const msg = JSON.parse(evt.data);
        if (msg.event === "didReceiveSettings") {
            const s = msg.payload.settings || {};
            if (s.metric && el("metric")) el("metric").value = s.metric;
            if (s.theme && el("theme")) el("theme").value = s.theme;
            if (s.interval && el("interval")) el("interval").value = s.interval;
            if (s.invert !== undefined && el("invert")) el("invert").checked = !!s.invert;
            if (s.step && el("step")) el("step").value = s.step;
        }
    };
}
function sendSettings() {
    const p = {};
    if (el("metric")) p.metric = el("metric").value;
    if (el("theme")) p.theme = el("theme").value;
    if (el("interval")) p.interval = parseFloat(el("interval").value);
    if (el("invert")) p.invert = el("invert").checked ? 1 : 0;
    if (el("step")) p.step = parseInt(el("step").value, 10) || 1;
    websocket.send(JSON.stringify({ event: "setSettings", context: context, payload: p }));
}
document.addEventListener("DOMContentLoaded", function() {
    ["metric", "theme", "interval", "invert", "step"].forEach(function(id) {
        if (el(id)) el(id).onchange = sendSettings;
    });
});
