document.addEventListener('DOMContentLoaded', function () {
    document.getElementById('campaignForm').onsubmit = function (event) {
        event.preventDefault();
        submitGenerateCodesForCart();
    };

    document.getElementById('campaignName').addEventListener('keydown', function (event) {
        if (event.key === 'Enter') {
            event.preventDefault();
            submitGenerateCodesForCart();
        }
    });
});

function submitGenerateCodesForCart() {
    var campaignName = document.getElementById('campaignName').value;
    var productIds = Array.from(document.querySelectorAll('.table-products tbody tr'))
        .map(tr => parseInt(tr.getAttribute('data-id')));

    fetch('/CartCode/GenerateCodesForCart', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': document.getElementsByName('__RequestVerificationToken')[0].value
        },
        body: JSON.stringify({ CampaignName: campaignName, ProductIds: productIds })
    })
        .then(response => {
            if (response.ok) {
                return response.json();
            } else {
                throw new Error('Server responded with status: ' + response.status);
            }
        })
        .then(data => {
            if (data.error) {
                showAlert(data.error);
            } else if (data.redirectUrl) {
                window.location.href = data.redirectUrl;
            } else {
                showAlert('Otrzymano nieoczekiwan¹ odpowiedŸ: ' + JSON.stringify(data));
            }
        })
        .catch(error => {
            showAlert('Wyst¹pi³ b³¹d po³¹czenia z serwerem: ' + error.message);
        });
}

function showAlert(message) {
    let alertPlaceholder = document.getElementById('alertContainer');
    if (!alertPlaceholder) {
        alertPlaceholder = document.createElement('div');
        alertPlaceholder.id = 'alertContainer';
        document.body.appendChild(alertPlaceholder);
    }

    alertPlaceholder.innerHTML = '';

    const wrapper = document.createElement('div');
    wrapper.innerHTML = [
        `<div class="alert alert-danger d-flex align-items-center justify-content-center" role="alert" style="min-height: 50px; max-width: 80%; margin: 0 auto; color: #FFF; background: rgba(255, 0, 0, 0.76); padding: 0.4rem 1.2rem; position: fixed; bottom: 50px; left: 50%; transform: translateX(-50%); z-index: 1000;">`,
        `   <div class="text-center">${message}</div>`,
        '</div>'
    ].join('');

    alertPlaceholder.append(wrapper);

    setTimeout(() => {
        wrapper.remove();
    }, 5000);
}
