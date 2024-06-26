document.addEventListener("DOMContentLoaded", function () {
    const updatePreview = () => {
        const buttonText = document.getElementById('ButtonText').value;
        const buttonColor = document.getElementById('ButtonColor').value || "#0E7E87";
        const buttonTextColor = document.getElementById('ButtonTextColor').value || "#FFFFFF";
        const frameTextColor = document.getElementById('FrameTextColor').value || "#000000";
        const frameColor = document.getElementById('FrameColor').value || "#E8E8E8";
        const extraTextColor = document.getElementById('FrameExtraTextColor').value || "#00B012";

        const previewButtons = document.querySelectorAll('.previewButton');
        previewButtons.forEach(previewButton => {
            previewButton.textContent = buttonText;
            previewButton.style.backgroundColor = buttonColor;
            previewButton.style.color = buttonTextColor;
        });

        const previewFrames = document.querySelectorAll('.previewFrame');
        previewFrames.forEach(previewFrame => {
            previewFrame.style.backgroundColor = frameColor;
        });

        const previewFrameTexts = document.querySelectorAll('.previewFrameText');
        previewFrameTexts.forEach(previewFrameText => {
            previewFrameText.style.color = frameTextColor;
        });

        const previewFrameExtraTexts = document.querySelectorAll('.previewFrameExtraText');
        previewFrameExtraTexts.forEach(previewFrameExtraText => {
            previewFrameExtraText.style.color = extraTextColor;
        });
    };

    ['ButtonText', 'ButtonTextColor', 'ButtonColor', 'FrameTextColor', 'FrameColor', 'FrameExtraTextColor'].forEach(id => {
        const element = document.getElementById(id);
        if (element) {
            element.addEventListener(id === 'ButtonText' ? 'change' : 'input', updatePreview);
        }
    });

    document.querySelectorAll('.style-row').forEach(row => {
        row.addEventListener('click', function () {
            const styleId = this.getAttribute('data-styleid');
            const name = this.getAttribute('data-name');
            const buttonText = this.getAttribute('data-button-text');
            const buttonColor = this.getAttribute('data-button-color');
            const buttonTextColor = this.getAttribute('data-button-text-color');
            const frameTextColor = this.getAttribute('data-frame-text-color');
            const frameColor = this.getAttribute('data-frame-color');
            const extraTextColor = this.getAttribute('data-extra-text-color');

            document.getElementById('styleId').value = styleId;
            document.getElementById('campaignName').value = name;
            document.getElementById('ButtonText').value = buttonText;
            document.getElementById('ButtonColor').value = buttonColor;
            document.getElementById('ButtonTextColor').value = buttonTextColor;
            document.getElementById('FrameTextColor').value = frameTextColor;
            document.getElementById('FrameColor').value = frameColor;
            document.getElementById('FrameExtraTextColor').value = extraTextColor;

            updatePreview();
        });
    });
});

document.addEventListener('DOMContentLoaded', function () {
    const elements = document.querySelectorAll('.resizable');

    elements.forEach(el => {
        let startX, startWidth;

        el.addEventListener('mousedown', function (e) {
            startX = e.clientX;
            startWidth = parseInt(document.defaultView.getComputedStyle(el).width, 10);
            function doDrag(e) {
                el.style.width = (startWidth - (e.clientX - startX)) + 'px';
            }

            function stopDrag() {
                document.documentElement.removeEventListener('mousemove', doDrag, false);
                document.documentElement.removeEventListener('mouseup', stopDrag, false);
            }

            document.documentElement.addEventListener('mousemove', doDrag, false);
            document.documentElement.addEventListener('mouseup', stopDrag, false);
        });
    });
});