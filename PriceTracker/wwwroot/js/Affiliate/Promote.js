document.addEventListener('DOMContentLoaded', function () {
    const checkboxes = document.querySelectorAll("input[name='selectedCategories']");
    const products = document.querySelectorAll(".Product-Container");
    const showAllProductsButton = document.getElementById('showAllProducts');
    initializeCartUI();

    function initializeCartUI() {
        const cart = JSON.parse(localStorage.getItem('cart')) || [];

        cart.forEach(productId => {
            const addToCartButton = document.querySelector(`.add-to-cart[data-product-id="${productId}"]`);
            const addedButton = document.querySelector(`.added-to-cart[data-product-id="${productId}"]`);

            if (addToCartButton) {
                addToCartButton.style.display = "none";
            }

            if (addedButton) {
                addedButton.style.display = "block";
            }
        });
        const cartList = document.querySelector('.Generate-Box-Box-List');
        cartList.innerHTML = '';

        cart.forEach(productId => {
            addProductToCartUI(productId);
        });
    }

    checkboxes.forEach(checkbox => {
        checkbox.addEventListener('change', filterProducts);
    });

    function filterProducts() {
        const selectedCategories = Array.from(checkboxes)
            .filter(checkbox => checkbox.checked)
            .map(checkbox => checkbox.value);

        if (selectedCategories.length === 0) {
            products.forEach(product => product.style.display = "");
        } else {
            products.forEach(product => {
                const categoryId = product.getAttribute('data-category-id');
                product.style.display = selectedCategories.includes(categoryId) ? "" : "none";
            });
        }
    }

    showAllProductsButton.addEventListener('click', function () {
        checkboxes.forEach(checkbox => checkbox.checked = false);

        products.forEach(product => product.style.display = "");
    });
});

const addToCartButtons = document.querySelectorAll('.add-to-cart');
addToCartButtons.forEach(button => {
    button.addEventListener('click', function () {
        const productId = button.getAttribute('data-product-id');

        addToCart(productId, function (success) {
            if (success) {
                button.style.display = "none";
                const addedButton = document.querySelector(`.added-to-cart[data-product-id="${productId}"]`);
                if (addedButton) {
                    addedButton.style.display = "block";
                }
            } else {
            }
        });
    });
});

function getProductData(productId) {
    return fetch('/Promote/GetProductData?productId=' + productId)
        .then(response => {
            if (!response.ok) {
                throw new Error('Network response was not ok');
            }
            return response.json();
        })
        .then(data => {
            return {
                productId: data.productId,
                productName: data.productName,
                productImage: data.productImage,
            };
        })
        .catch(error => {
            console.error('There has been a problem with your fetch operation:', error);
        });
}

function addProductToCartUI(productId) {
    const cartList = document.querySelector('.Generate-Box-Box-List');

    getProductData(productId).then(productData => {
        const cartItem = `
                    <div class="cart-item" data-product-id="${productId}">
                        <img src="${productData.productImage}" alt="${productData.productName}" width="50" height="50" />
                        ${productData.productName}
                        <button onclick="removeFromCart('${productId}')" class="remove-from-cart" data-product-id="${productId}">Usuń</button>
                    </div>
                `;
        cartList.innerHTML += cartItem;
    }).catch(error => {
        console.error('Error fetching product data:', error);
    });
}

function removeProductFromCartUI(productId) {
    const cartList = document.querySelector('.Generate-Box-Box-List');
    const productElement = cartList.querySelector(`.cart-item[data-product-id="${productId}"]`);
    if (productElement) {
        cartList.removeChild(productElement);
    }
}

function addToCart(productId, callback) {
    let cart = JSON.parse(localStorage.getItem('cart')) || [];
    const maxProductsAllowed = 16;

    if (cart.length >= maxProductsAllowed && !cart.includes(productId)) {
        showAlert("Osiągnąłeś limit 16 produktów w generatorze. Usuń niektóre produkty, aby dodać nowe.");
        callback(false);
        return;
    }

    if (!cart.includes(productId)) {
        cart.push(productId);
        localStorage.setItem('cart', JSON.stringify(cart));
        addProductToCartUI(productId);
        callback(true);
    } else {
        callback(false);
    }
}

function removeFromCart(productId) {
    let cart = JSON.parse(localStorage.getItem('cart')) || [];
    const maxProductsAllowed = 16;

    const index = cart.indexOf(productId);
    if (index > -1) {
        cart.splice(index, 1);
        localStorage.setItem('cart', JSON.stringify(cart));

        removeProductFromCartUI(productId);

        if (cart.length < maxProductsAllowed) {
            document.querySelectorAll('.add-to-cart').forEach(button => {
                button.disabled = false;
                button.style.pointerEvents = 'auto';
            });
        }

        const addToCartButton = document.querySelector(`.add-to-cart[data-product-id="${productId}"]`);
        if (addToCartButton) {
            addToCartButton.style.display = "block";
        }
        const addedButton = document.querySelector(`.added-to-cart[data-product-id="${productId}"]`);
        if (addedButton) {
            addedButton.style.display = "none";
        }
    }
}

const generateLink = document.getElementById('generateLink');
generateLink.addEventListener('click', function (e) {
    e.preventDefault();
    goToGenerate();
});

function goToGenerate() {
    const cart = JSON.parse(localStorage.getItem('cart')) || [];
    const maxProductsAllowed = 16;

    if (cart.length === 0) {
        showAlert("Musisz dodać przynajmniej jeden produkt do generatora, aby wygenerować kampanię.");
    } else if (cart.length > maxProductsAllowed) {
        showAlert(`Możesz dodać maksymalnie ${maxProductsAllowed} produktów do koszyka.`);
    } else {
        window.location.href = '/Promote/Generate?ids=' + cart.join(',');
    }
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
    }, 3000);
}

$(document).ready(function () {
    $('#productSearch').on('keyup', function () {
        var searchText = $(this).val().toLowerCase();

        $('.Product-Container').filter(function () {
            $(this).toggle($(this).find('.Product-Container-Data-Top').text().toLowerCase().indexOf(searchText) > -1);
        });
    });
});

let lastDirection = ''; 

function sortProducts(direction) {
    const container = document.querySelector('.Products-Box-Product');
    let products = document.querySelectorAll('.Product-Container');
    products = Array.from(products);


    document.querySelectorAll('.button-sort').forEach(button => {
        button.style.background = '#F1F1F1';
    });

   
    if (direction === lastDirection) {
        lastDirection = ''; 
    } else {
        lastDirection = direction; 
        document.getElementById(`sort-${direction}`).style.background = '#DADADA'; 

        
        products.sort((a, b) => {
            const commissionA = parseInt(a.querySelector('.Product-Container-Data-Bottom-Left-Up').textContent.trim().replace(' zł', ''), 10);
            const commissionB = parseInt(b.querySelector('.Product-Container-Data-Bottom-Left-Up').textContent.trim().replace(' zł', ''), 10);

            return direction === 'asc' ? commissionA - commissionB : commissionB - commissionA;
        });
    }

    
    while (container.firstChild) {
        container.removeChild(container.firstChild);
    }

    products.forEach(product => {
        container.appendChild(product);
    });
}