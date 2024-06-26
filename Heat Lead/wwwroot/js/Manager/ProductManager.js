document.addEventListener('DOMContentLoaded', function () {
    var container = document.getElementById('productIdStoresContainer');
    var addButton = document.querySelector('button[onclick="addProductIdStore()"]');
    var categoryCommissionPercentage = null;

    function updateUI() {
        const allInputGroups = container.querySelectorAll('.input-group');
        addButton.style.display = allInputGroups.length >= 8 ? 'none' : 'block';
        allInputGroups.forEach(group => {
            group.querySelector('.remove-btn').style.display = allInputGroups.length > 1 ? 'block' : 'none';
        });
    }

    function createInputGroup(value = '') {
        var inputGroup = document.createElement('div');
        inputGroup.classList.add('input-group');

        var input = document.createElement('input');
        input.type = 'text';
        input.name = 'ProductIdStores';
        input.classList.add('form-control');
        input.value = value;

        var removeBtn = document.createElement('button');
        removeBtn.textContent = 'X';
        removeBtn.type = 'button';
        removeBtn.classList.add('remove-btn');
        removeBtn.addEventListener('click', function () {
            inputGroup.remove();
            updateUI();
        });

        inputGroup.appendChild(input);
        inputGroup.appendChild(removeBtn);

        return inputGroup;
    }

    function addProductIdStore(value = '') {
        if (container.querySelectorAll('.input-group').length >= 8) {
            return;
        }

        var inputGroup = createInputGroup(value);
        container.appendChild(inputGroup);
        updateUI();
    }

    addButton.addEventListener('click', function () { addProductIdStore(); });

    const existingInputs = [...container.querySelectorAll('input[name="ProductIdStores"]')];
    if (existingInputs.length > 0) {
        existingInputs.forEach(input => input.remove());
    }

    existingInputs.forEach(input => addProductIdStore(input.value));

    if (container.querySelectorAll('.input-group').length === 0) {
        addProductIdStore();
    }

    var form = document.querySelector('form');
    form.addEventListener('submit', function (event) {
        const inputs = container.querySelectorAll('input[name="ProductIdStores"]');
        const filledInputs = Array.from(inputs).filter(input => input.value.trim() !== '');

        inputs.forEach(input => {
            if (input.value.trim() === '') {
                input.parentNode.remove();
            }
        });

        if (filledInputs.length === 0 && form.hasAttribute('action') && form.getAttribute('action').includes('AddProduct')) {
            event.preventDefault();
            alert('Proszę dodać co najmniej jedno ID produktu w sklepie.');
        }
    });

    function recalculateCommission() {
        var productPrice = parseFloat($("#ProductPrice").val());
        if (!isNaN(productPrice) && productPrice > 0 && categoryCommissionPercentage !== null) {
            var commission = productPrice * (categoryCommissionPercentage / 100);
            $("#AffiliateCommission").val(commission.toFixed(2));
        }
    }

    function updateImageDisplay() {
        var imageUrl = document.getElementById('productImageInput').value;
        var imageDisplay = document.getElementById('productImageDisplay');

        if (imageUrl) {
            imageDisplay.src = imageUrl;
        } else {
            imageDisplay.src = noPhotoUrl;
        }
    }

    document.getElementById('productImageInput').addEventListener('input', updateImageDisplay);
    updateImageDisplay();

    $("#storeDropdown").change(function () {
        var storeId = $(this).val();
        $.getJSON(`/ProductAssignment/GetCategories/${storeId}`, function (data) {
            var $categoryDropdown = $("#categoryDropdown");
            $categoryDropdown.empty();
            $categoryDropdown.append('<option value="">Wybierz kategorię</option>');
            $.each(data, function (index, item) {
                $categoryDropdown.append(`<option value="${item.categoryId}">${item.categoryName}</option>`);
            });
        });
    });

    $("#categoryDropdown").change(function () {
        var categoryId = $(this).val();
        if (categoryId) {
            $.getJSON(`/ProductAssignment/GetCategoryCommission/${categoryId}`, function (data) {
                if (data.commissionPercentage) {
                    categoryCommissionPercentage = data.commissionPercentage;
                    $("#categoryCommissionPercentage").text(categoryCommissionPercentage);
                    recalculateCommission();
                } else {
                    $("#categoryCommissionPercentage").text("brak danych");
                    categoryCommissionPercentage = null;
                }
            });
        }
    });

    $("#ProductPrice").on('input', function () {
        recalculateCommission();
    });

    var $storeDropdown = $('#storeDropdown');
    if ($storeDropdown.find('option').length > 1) {
        $storeDropdown.find('option:eq(1)').prop('selected', true).change();
    }
});

$(document).ready(function () {
    var initialStoreId = $("#storeDropdown").val();
    if (initialStoreId) {
        $("#storeDropdown").change();
    }
});
