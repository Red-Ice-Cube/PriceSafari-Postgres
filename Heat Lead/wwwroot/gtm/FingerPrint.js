function getCanvasFingerprint() {
    return new Promise(resolve => {
        const canvas = document.createElement('canvas');
        const ctx = canvas.getContext('2d');

        canvas.width = 240;
        canvas.height = 60;
        ctx.textBaseline = "alphabetic";
        ctx.fillStyle = "#f60";
        ctx.fillRect(100, 1, 62, 20);
        ctx.fillStyle = "#069";

        ctx.font = '11pt "Times New Roman"';
        let text = "Cwm fjord bank glyphs vext quiz";
        ctx.fillText(text, 2, 15);
        ctx.fillStyle = "rgba(102, 204, 0, 0.7)";
        ctx.font = "18pt Arial";
        ctx.fillText(text, 4, 45);

        ctx.globalCompositeOperation = 'multiply';
        ctx.fillStyle = 'rgb(255,0,255)';
        ctx.beginPath();
        ctx.arc(50, 50, 50, 0, Math.PI * 2, true);
        ctx.closePath();
        ctx.fill();
        ctx.fillStyle = 'rgb(0,255,255)';
        ctx.beginPath();
        ctx.arc(100, 50, 50, 0, Math.PI * 2, true);
        ctx.closePath();
        ctx.fill();
        ctx.fillStyle = 'rgb(255,255,0)';
        ctx.beginPath();
        ctx.arc(75, 100, 50, 0, Math.PI * 2, true);
        ctx.closePath();
        ctx.fill();

        ctx.globalCompositeOperation = 'destination-over';
        ctx.fillRect(0, 0, 240, 60);

        const dataUrl = canvas.toDataURL();
        resolve(hashString(dataUrl));
    });
}

function getWebGLFingerprint() {
    return new Promise(resolve => {
        var canvas = document.createElement('canvas');
        var gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');
        if (!gl) {
            resolve('WebGL not supported');
            return;
        }
        var debugInfo = gl.getExtension('WEBGL_debug_renderer_info');
        var vendor = gl.getParameter(debugInfo.UNMASKED_VENDOR_WEBGL);
        var renderer = gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL);
        resolve('WebGL Vendor: ' + vendor + '; WebGL Renderer: ' + renderer);
    });
}

function getWebRTCFingerprint() {
    return new Promise((resolve) => {
        var rtcPeerConnection = window.RTCPeerConnection || window.mozRTCPeerConnection || window.webkitRTCPeerConnection;
        if (!rtcPeerConnection) {
            resolve('WebRTC not supported');
            return;
        }

        var pc = new rtcPeerConnection({
            iceServers: [{ urls: "stun:stun.l.google.com:19302" }]
        });
        var ips = new Set();
        pc.createDataChannel("");
        pc.createOffer().then(offer => pc.setLocalDescription(offer));
        pc.onicecandidate = function (event) {
            if (event.candidate) {
                var match = /([0-9]{1,3}(\.[0-9]{1,3}){3}|[a-f0-9]{1,4}(:[a-f0-9]{1,4}){7})/g.exec(event.candidate.candidate);
                if (match) {
                    ips.add(match[1]);
                }
            } else {
                resolve(Array.from(ips).join(', ') || "unknown");
                pc.close();
            }
        };
    });
}

async function hashString(string) {
    const encoder = new TextEncoder();
    const data = encoder.encode(string);
    const hashBuffer = await crypto.subtle.digest('SHA-256', data);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    const hashHex = hashArray.map(byte => byte.toString(16).padStart(2, '0')).join('');
    return hashHex;
}

function getUserAgent() {
    return navigator.userAgent;
}

function getBrowserPlugins() {
    var plugins = [];
    for (var i = 0; i < navigator.plugins.length; i++) {
        plugins.push(navigator.plugins[i].name);
    }
    return plugins.join(', ');
}

function getTouchSupport() {
    var touchSupport = {
        hasTouch: ('ontouchstart' in window || navigator.maxTouchPoints > 0 || navigator.msMaxTouchPoints > 0),
        maxTouchPoints: 0
    };
    if (navigator.maxTouchPoints) {
        touchSupport.maxTouchPoints = navigator.maxTouchPoints;
    } else if (navigator.msMaxTouchPoints) {
        touchSupport.maxTouchPoints = navigator.msMaxTouchPoints;
    }
    return touchSupport;
}

function hasSessionStorage() {
    try {
        return !!window.sessionStorage;
    } catch (e) {
        return false;
    }
}

function hasLocalStorage() {
    try {
        return !!window.localStorage;
    } catch (e) {
        return false;
    }
}

function hasIndexedDB() {
    try {
        return !!window.indexedDB;
    } catch (e) {
        return false;
    }
}

function hasAddBehavior() {
    return !!document.body.addBehavior;
}

function hasOpenDatabase() {
    return !!window.openDatabase;
}

function detectFonts() {
    const baseFonts = ['monospace', 'sans-serif', 'serif'];
    const fontList = ['Arial', 'Arial Black', 'Comic Sans MS', 'Courier New', 'Georgia', 'Impact', 'Times New Roman', 'Trebuchet MS', 'Verdana', 'Webdings'];
    const testString = "mmmmmmmmmmlli";
    const testSize = '72px';

    const h = document.getElementsByTagName("body")[0];
    const baseDivs = baseFonts.map(font => {
        let s = document.createElement("span");
        s.style.fontSize = testSize;
        s.innerHTML = testString;
        s.style.fontFamily = font;
        h.appendChild(s);
        return s;
    });

    const detectedFonts = fontList.filter(font => {
        let hasFont = false;
        let s = document.createElement("span");
        s.style.fontSize = testSize;
        s.innerHTML = testString;
        s.style.fontFamily = `${font},${baseFonts[0]}`;
        h.appendChild(s);

        hasFont = baseDivs.every(baseDiv => s.offsetWidth !== baseDiv.offsetWidth || s.offsetHeight !== baseDiv.offsetHeight);

        s.remove();
        return hasFont;
    });

    baseDivs.forEach(div => div.remove());
    return detectedFonts;
}

function getLogicalProcessors() {
    return navigator.hardwareConcurrency || 'unknown';
}

function getDeviceMemory() {
    return navigator.deviceMemory || 'unknown';
}

async function getCameraInfo() {
    try {
        const devices = await navigator.mediaDevices.enumerateDevices();
        return devices.filter(device => device.kind === 'videoinput');
    } catch (error) {
        return 'Camera access denied or not available';
    }
}

function gatherDeviceInfo() {
    var touchInfo = getTouchSupport();

    var screenData = {
        screenWidth: window.screen.width,
        screenHeight: window.screen.height,
        colorDepth: window.screen.colorDepth,
        devicePixelRatio: window.devicePixelRatio,
        pixelCount: window.screen.width * window.screen.height,
        touchSupport: touchInfo.hasTouch,
        maxTouchPoints: touchInfo.maxTouchPoints,
        browserLanguage: navigator.language,
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        platform: navigator.platform,
        userAgent: getUserAgent(),
        browserPlugins: getBrowserPlugins()
    };
    return screenData;
}

function gatherExtendedDeviceInfo() {
    const deviceInfo = {
        hasSessionStorage: hasSessionStorage(),
        hasLocalStorage: hasLocalStorage(),
        hasIndexedDB: hasIndexedDB(),
        hasAddBehavior: hasAddBehavior(),
        hasOpenDatabase: hasOpenDatabase(),
        logicalProcessors: getLogicalProcessors(),
        deviceMemory: getDeviceMemory(),
    };

    return Promise.all([
        detectFonts(),
        getCameraInfo()
    ]).then(([detectedFonts, cameraInfo]) => {
        deviceInfo.detectedFonts = detectedFonts;
        deviceInfo.cameraInfo = cameraInfo;

        return deviceInfo;
    }).catch(error => {
        return deviceInfo;
    });
}

function gatherAndSendFingerprintData() {
    Promise.all([
        getCanvasFingerprint(),
        getWebGLFingerprint(),
        getWebRTCFingerprint(),
        gatherDeviceInfo(),
        gatherExtendedDeviceInfo()
    ]).then(results => {
        var canvasFingerprint = results[0];
        var webGLFingerprint = results[1];
        var webRTCFingerprint = results[2];
        var basicDeviceInfo = results[3];
        var extendedDeviceInfo = results[4];
        var completeDeviceInfo = Object.assign({}, basicDeviceInfo, extendedDeviceInfo);

        sendFingerprintDataToAPI(canvasFingerprint, webGLFingerprint, webRTCFingerprint, completeDeviceInfo);
    }).catch(error => {
        console.error('Error in fingerprint generation:', error);
    });
}

function sendFingerprintDataToAPI(canvasFingerprint, webGLFingerprint, webRTCFingerprint, deviceInfo) {
    var heatleadValue = localStorage.getItem('heatleadValue');
    var sessionToken = localStorage.getItem('sessionToken');
    var dataToSend = {
        HeatLeadTrackingCode: heatleadValue,
        SessionToken: sessionToken,
        CanvasFingerprint: canvasFingerprint,
        WebGLFingerprint: webGLFingerprint,
        WebRTCFingerprint: webRTCFingerprint,
        ScreenWidth: deviceInfo.screenWidth,
        ScreenHeight: deviceInfo.screenHeight,
        pixelCount: deviceInfo.pixelCount,
        ColorDepth: deviceInfo.colorDepth,
        DevicePixelRatio: deviceInfo.devicePixelRatio,
        TouchSupport: deviceInfo.touchSupport,
        MaxTouchPoints: deviceInfo.maxTouchPoints,
        BrowserLanguage: deviceInfo.browserLanguage,
        TimeZone: deviceInfo.timeZone,
        Platform: deviceInfo.platform,
        UserAgent: deviceInfo.userAgent,
        BrowserPlugins: deviceInfo.browserPlugins,
        HasSessionStorage: deviceInfo.hasSessionStorage,
        HasLocalStorage: deviceInfo.hasLocalStorage,
        HasIndexedDB: deviceInfo.hasIndexedDB,
        HasAddBehavior: deviceInfo.hasAddBehavior,
        HasOpenDatabase: deviceInfo.hasOpenDatabase,
        LogicalProcessors: deviceInfo.logicalProcessors.toString(),
        DeviceMemory: deviceInfo.deviceMemory.toString(),
        DetectedFonts: JSON.stringify(deviceInfo.detectedFonts),
        CameraInfo: JSON.stringify(deviceInfo.cameraInfo)
    };

    var xhr = new XMLHttpRequest();
    xhr.open('POST', 'https://eksperci.myjki.com/api/fingerprint', true);
    xhr.setRequestHeader('Content-Type', 'application/json; charset=UTF-8');
    xhr.send(JSON.stringify(dataToSend));
    xhr.onload = function () {
        if (xhr.status >= 200 && xhr.status < 300) {
        } else {
        }
    };
    xhr.onerror = function () {
    };
}

gatherAndSendFingerprintData();