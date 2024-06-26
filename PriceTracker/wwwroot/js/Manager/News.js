function updateImageDisplay() {
    var imageUrl = document.getElementById('newsImageInput').value;
    var imageDisplay = document.getElementById('newsImageDisplay');

    if (imageUrl) {
        imageDisplay.src = imageUrl;
    } else {
        imageDisplay.src = '@Url.Content("~/images/No-Photo.jpg")';
    }
}

document.addEventListener('DOMContentLoaded', updateImageDisplay);

document.getElementById('newsImageInput').addEventListener('input', updateImageDisplay);

$(document).ready(function () {
    $('.decimal-field').on('input', function () {
        this.value = this.value.replace(/[^0-9\,]/g, '').replace(/(\..*?)\..*/g, '$1');
    });
});

CKEDITOR.replace('Message', {
    toolbar: [
        { name: 'basicstyles', items: ['Bold'] },
        { name: 'links', items: ['Link', 'Unlink'] }
    ],

    removeButtons: ''
});

$(document).ready(function () {
    $('#productSearch').on('keyup', function () {
        var searchText = $(this).val().toLowerCase();

        $('.table-orders tbody tr').each(function () {
            var productName = $(this).find('td:nth-child(2)').text().toLowerCase();
            $(this).toggle(productName.indexOf(searchText) > -1);
        });
    });
});