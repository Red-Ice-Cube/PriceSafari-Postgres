(function () {
    var scriptTag = document.currentScript;
    var secCan = scriptTag.getAttribute('data-canvas-id');

    function createCheckStoreButton(data) {

        var button = document.createElement("div");
        button.style.display = "flex";
        button.style.alignItems = "center";
        button.style.justifyContent = "center";
        button.style.padding = "8px";
        button.style.textAlign = "center";
        button.style.borderRadius = "4px";
        button.style.backgroundColor = data.style.buttonColor;
        button.style.color = data.style.buttonTextColor;
        button.style.fontSize = "clamp(12px, 3vw, 16px)";
        button.style.fontWeight = "500";
        button.style.cursor = "pointer";
        button.style.overflow = "hidden";
        button.style.lineHeight = "1.2em";
        button.style.whiteSpace = "nowrap";
        button.textContent = data.style.buttonText;
        button.onclick = function () {
            window.open(data.url, '_blank');
        };

        scriptTag.parentNode.insertBefore(button, scriptTag.nextSibling);
    }

    fetch(`https://eksperci.myjki.com/api/settings/JS-CAN-get?secCan=${secCan}`)
        .then(response => response.json()) 
        .then(data => {
           
            createCheckStoreButton(data); 
        })
    
})();
