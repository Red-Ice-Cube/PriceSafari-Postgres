
function freezeGif() {
    var banner = document.getElementById("homeBanner");
    if (banner) {
        var frozenFrameSrc = banner.src.replace('.gif', '.png');
        var preloadedImage = document.getElementById("preloadedImage");

        if (preloadedImage && preloadedImage.complete) {
            banner.src = frozenFrameSrc;
        } else if (preloadedImage) {
            preloadedImage.onload = function () {
                banner.src = frozenFrameSrc;
            };
        }
    }
}

setTimeout(freezeGif, 5000);

