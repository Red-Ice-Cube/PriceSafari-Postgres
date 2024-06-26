
(async function () {
    function validateOrderKey(key) {
        return /^[a-zA-Z0-9]{1,40}$/.test(key);
    }

    function getOrderKeyFromUrl() {
        const urlParams = new URLSearchParams(window.location.search);
        const orderKey = urlParams.get('key') || "unknown";
        if (!validateOrderKey(orderKey)) {
           
            return "unknown";
        }
        return orderKey;
    }

    function getOrderIdFromUrl() {
        const url = window.location.href;
        const match = url.match(/id_order=(\d+)/);
        return match ? match[1] : "unknown";
    }

    async function getHLTC() {
        const hLTCJSON = localStorage.getItem('HLTC');
        if (hLTCJSON) {
            try {
                const parsedData = JSON.parse(hLTCJSON);
                if (parsedData.heatlead && parsedData.token && parsedData.set && parsedData.expires) {
                    return {
                        heatLead: parsedData.heatlead,
                        token: parsedData.token,
                        setTime: parsedData.set,
                        expires: parsedData.expires
                    };
                }
            } catch (e) {
              
            }
        }
        return null;
    }

    async function handleDataSubmission() {
        const orderId = getOrderIdFromUrl();
        const orderKey = getOrderKeyFromUrl();
        const tD = await getHLTC();

        if (!tD) {
     
            return;
        }

        const data = {
            OrderId: orderId,
            OrderKey: orderKey,
            HLTC: tD.heatLead,
            Token: tD.token,
            SetTime: tD.setTime,
            Expires: tD.expires
            
        };


        postDataToAPI(data);
    }

    function postDataToAPI(data) {
        const xhr = new XMLHttpRequest();
        xhr.open('POST', 'https://eksperci.myjki.com/api/interceptorder', true);
        xhr.setRequestHeader('Content-Type', 'application/json; charset=UTF-8');
        xhr.send(JSON.stringify(data));

        xhr.onload = function () {
       
        };

        xhr.onerror = function () {
         
        };
    }

    handleDataSubmission();
})();







