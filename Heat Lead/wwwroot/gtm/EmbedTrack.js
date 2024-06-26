(async function () {
    function generateSessionToken() {
        var characters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
        var tokenLength = 24;
        var token = '';

        for (var i = 0; i < tokenLength; i++) {
            var randomIndex = Math.floor(Math.random() * characters.length);
            token += characters.charAt(randomIndex);
        }

        localStorage.setItem('sessionToken', token);
        return token;
    }

    var params = new URLSearchParams(window.location.search);
    var heatleadValue = params.get('heatlead');
    localStorage.setItem('heatleadValue', heatleadValue);

    var sessionToken = generateSessionToken();

    function registerEntryAndLocalStorage(heatleadValue, sessionToken, token) {
        if (heatleadValue) {
            fetch('https://eksperci.myjki.com/api/settings/TTL')
                .then(response => response.json())
                .then(tTL => {
                    var days = tTL || 7;
                    var currentDate = new Date();
                    var expiresDate = new Date(currentDate.getTime() + days * 86400 * 1000);

                    const userData = {
                        heatlead: heatleadValue,
                        set: currentDate.toISOString(),
                        expires: expiresDate.toISOString(),
                        token: sessionToken
                    };

                    localStorage.setItem('HLTC', JSON.stringify(userData));

                    fetch('https://eksperci.myjki.com/api/link-loaded', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'Authorization': `Bearer ${token}`
                        },
                        body: JSON.stringify({
                            heatlead: heatleadValue,
                            LocalStorageSet: true,
                            sessionToken: sessionToken
                        })
                    });
                });
        }
    }

    async function fetchAndRegister() {
        if (heatleadValue) {
            const response = await fetch(`https://eksperci.myjki.com/api/generate-token?heatlead=${heatleadValue}`);
            const data = await response.json();
            const token = data.token;

            registerEntryAndLocalStorage(heatleadValue, sessionToken, token);
        }
    }

    await fetchAndRegister();

    async function loadFingerprintScript() {
        try {
            const response = await fetch('https://eksperci.myjki.com/api/settings/Fp-col');
            if (response.ok) {
                const settings = await response.json();
                if (settings === true) {
                    var script = document.createElement('script');
                    script.src = "https://eksperci.myjki.com/gtm/fingerprint.min.js";
                    document.head.appendChild(script);
                }
            }
        } catch (error) { }
    }

    loadFingerprintScript();
})();
