function copyToClipboard(elementId) {
    var textToCopy = document.getElementById(elementId).innerText;
    var textArea = document.createElement("textarea");
    textArea.value = textToCopy;
    textArea.style.position = 'fixed';
    textArea.style.left = '0';
    textArea.style.top = '0';
    textArea.style.opacity = '0';
    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();

    try {
        var successful = document.execCommand('copy');
        var msg = successful ? 'Skopiowano: ' + textToCopy : 'Nie udało się skopiować.';
        showCopyAlert(msg, successful ? 'success' : 'danger');
    } catch (err) {
        showCopyAlert('Nie udało się skopiować. ' + err, 'danger');
    }

    document.body.removeChild(textArea);
}

function showCopyAlert(message, alertType) {
    const alertPlaceholder = document.getElementById('alertContainer');

    alertPlaceholder.innerHTML = '';
    const wrapper = document.createElement('div');
    wrapper.innerHTML = [
        `<div class="alert alert-${alertType} d-flex align-items-center justify-content-center" role="alert" style="min-height: 50px; width: auto; padding: 0.4rem 1.2rem;">`,
        `   <div class="text-center">${message}</div>`,
        '</div>'
    ].join('');

    alertPlaceholder.append(wrapper);

    alertPlaceholder.style.position = 'fixed';
    alertPlaceholder.style.left = '50%';
    alertPlaceholder.style.bottom = '20px';
    alertPlaceholder.style.transform = 'translateX(-40%)';
    alertPlaceholder.style.display = 'flex';
    alertPlaceholder.style.justifyContent = 'center';
    alertPlaceholder.style.alignItems = 'center';

    setTimeout(() => {
        wrapper.remove();
    }, 3000);
}


