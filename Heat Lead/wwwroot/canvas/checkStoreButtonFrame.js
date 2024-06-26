(function () {
    var scriptTag = document.currentScript;
    var secCan = scriptTag.getAttribute('data-canvas-id');


    function createCheckStoreButtonFrame(data) {
        var frame = document.createElement("div");
        frame.style.display = "flex";
        frame.style.flexWrap = "wrap";
        frame.style.alignItems = "center";
        frame.style.justifyContent = "center";
        frame.style.padding = "10px";
        frame.style.gap = "16px";
        frame.style.borderRadius = "8px";
        frame.style.boxShadow = "0px 4px 4px 0px rgba(0, 0, 0, 0.25)";
        frame.style.background = data.style.frameColor;

        var textContainer = document.createElement("div");
        textContainer.style.display = "-webkit-box";
        textContainer.style.webkitLineClamp = "2";
        textContainer.style.webkitBoxOrient = "vertical";
        textContainer.style.overflow = "hidden";
        textContainer.style.textOverflow = "ellipsis";
        textContainer.style.fontWeight = "400";
        textContainer.style.lineHeight = "1.2";
        textContainer.style.maxHeight = "2.4em";
        textContainer.style.flexGrow = "1";
        textContainer.style.color = data.style.frameTextColor;
        textContainer.style.fontSize = "clamp(12px, 3.5vw, 16px)";
        textContainer.textContent = data.style.frameText;
        textContainer.style.minWidth = "120px";

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

        frame.appendChild(textContainer);
        frame.appendChild(button);
        scriptTag.parentNode.insertBefore(frame, scriptTag.nextSibling);

    }

    fetch(`https://eksperci.myjki.com/api/settings/JS-CAN-get?secCan=${secCan}`)
        .then(response => response.json())
        .then(data => {
    
            createCheckStoreButtonFrame(data);
        })
   
})();
