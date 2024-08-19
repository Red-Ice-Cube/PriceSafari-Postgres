document.addEventListener("DOMContentLoaded", function () {
    const buttons = document.querySelectorAll('.change-banner');
    const images = document.querySelectorAll('#banner-change-target .banner-img');

    buttons[0].classList.add('change-banner-active');

    buttons.forEach((button, index) => {
        button.addEventListener('click', function () {
            buttons.forEach(btn => btn.classList.remove('change-banner-active'));

            this.classList.add('change-banner-active');

            images.forEach(img => {
                img.style.display = 'none';
            });

            images[this.getAttribute('data-index')].style.display = 'block';
        });
    });
});

function toggleAnswer(element) {
    const currentIsActive = element.querySelector('.List-Box-Text-Text-Q').classList.contains('active');

    document.querySelectorAll('.List-Box-Text-Text-Q').forEach((el) => {
        el.style.display = "none";
        el.classList.remove('active');
    });

    if (!currentIsActive) {
        const answer = element.querySelector('.List-Box-Text-Text-Q');
        answer.classList.add('active');
        answer.style.display = "block";
    }
}