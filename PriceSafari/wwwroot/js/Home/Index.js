
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


function toggleMenu() {
    var menu = document.getElementById('mobileMenu');
    if (menu) {
        if (menu.style.display === 'flex') {
            menu.style.display = 'none';
        } else {
            menu.style.display = 'flex';
        }
    }
}

document.addEventListener("DOMContentLoaded", function () {
   
    const buttons1 = document.querySelectorAll('.change-banner-1');
    const images1 = document.querySelectorAll('#banner-change-target-1 .banner-img');

    if (buttons1.length > 0 && images1.length > 0) {
        buttons1[0].classList.add('change-banner-1-active'); 
        buttons1.forEach((button, index) => {
            button.addEventListener('click', function () {
                buttons1.forEach(btn => btn.classList.remove('change-banner-1-active'));
                this.classList.add('change-banner-1-active'); 
                images1.forEach(img => img.style.display = 'none'); 
                images1[index].style.display = 'block'; 
            });
        });
    }

   
    const buttons2 = document.querySelectorAll('.change-banner-2');
    const images2 = document.querySelectorAll('#banner-change-target-2 .banner-img');

    if (buttons2.length > 0 && images2.length > 0) {
        buttons2[0].classList.add('change-banner-2-active'); 

        buttons2.forEach((button, index) => {
            button.addEventListener('click', function () {
                buttons2.forEach(btn => btn.classList.remove('change-banner-2-active')); 
                this.classList.add('change-banner-2-active'); 
                images2.forEach(img => img.style.display = 'none'); 
                images2[index].style.display = 'block';
            });
        });
    }

   
    const modal = document.getElementById("image-modal");
    const modalImg = document.getElementById("modal-image");
    const closeBtn = document.querySelector("#image-modal .close");
    const bannerImages = document.querySelectorAll('.banner-img');

    if (modal && modalImg && closeBtn) {
        bannerImages.forEach(image => {
            image.addEventListener('click', function () {
                console.log("Clicked image:", this.src); 
                modal.style.display = "block";
                modalImg.src = this.src;
            });
        });

     
        closeBtn.addEventListener('click', function () {
            modal.style.display = "none";
        });

       
        modal.addEventListener('click', function (event) {
            if (event.target == modal) {
                modal.style.display = "none";
            }
        });
    }
});

function toggleAnswer(element) {
    const isActive = element.classList.contains('active');

 
    document.querySelectorAll('.List-Box-Q.active').forEach((el) => {
        el.classList.remove('active');
    });


    if (!isActive) {
        element.classList.add('active');
    }
}
