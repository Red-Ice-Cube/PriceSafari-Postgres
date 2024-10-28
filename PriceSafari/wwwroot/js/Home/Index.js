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
    const isActive = element.classList.contains('active');

    // Collapse any open items
    document.querySelectorAll('.List-Box-Q.active').forEach((el) => {
        el.classList.remove('active');
    });

    // If the clicked item was not active, activate it
    if (!isActive) {
        element.classList.add('active');
    }
}
