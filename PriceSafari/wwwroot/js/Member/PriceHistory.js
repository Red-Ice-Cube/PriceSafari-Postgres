document.addEventListener("DOMContentLoaded", function () {
    let allPrices = [];
    let chartInstance = null;
    let myStoreName = "";
    let setPrice1 = 2.00;
    let setPrice2 = 2.00;
    let setStepPrice = 2.00;
    let usePriceDifference = document.getElementById('usePriceDifference').checked;
    let marginSettings = {
        useMarginForSimulation: true,
        enforceMinimalMargin: true,
        minimalMarginPercent: 0.00
    };
    let selectedProductId = null;
    let competitorStore = "";
    let selectedFlags = new Set();
   
    let sortingState = {
        sortName: null,
        sortPrice: null,
        sortRaiseAmount: null,
        sortRaisePercentage: null,
        sortLowerAmount: null,
        sortLowerPercentage: null,
        sortMarginAmount: null,
        sortMarginPercentage: null,
        showRejected: null
    };



    let positionSlider;
    let offerSlider;

    const openMassChangeModalBtn = document.getElementById('openMassChangeModalBtn');
    const massChangeModal = document.getElementById('massChangeModal');
    const closeMassChangeModalBtn = document.querySelector('#massChangeModal .close'); // zmieniony selektor

    // Używając Bootstrapa i jQuery:
    openMassChangeModalBtn.addEventListener('click', function () {
        $('#massChangeModal').modal('show');
    });

    closeMassChangeModalBtn.addEventListener('click', function () {
        $('#massChangeModal').modal('hide');
    });

    // Jeśli klikniemy poza modalem (Bootstrap automatycznie zamyka modal, ale jeśli chcesz samodzielnie):
    window.addEventListener('click', function (event) {
        if (event.target === massChangeModal) {
            $('#massChangeModal').modal('hide');
        }
    });

    const massMatchBtn = document.getElementById('massMatchBtn');
    const massStrategicBtn = document.getElementById('massStrategicBtn');

    massMatchBtn.addEventListener('click', function () {
        applyMassChange('match');
        $('#massChangeModal').modal('hide');
    });

    massStrategicBtn.addEventListener('click', function () {
        applyMassChange('strategic');
        $('#massChangeModal').modal('hide');
    });
    function applyMassChange(changeType) {
    
        const boxes = Array.from(document.querySelectorAll('#priceContainer .price-box'));

        
        boxes.forEach(box => {
            const buttons = box.querySelectorAll('.simulate-change-btn');
            if (!buttons || buttons.length === 0) return;

            let targetButton;
            if (changeType === 'match' && buttons[0]) {
                targetButton = buttons[0]; // pierwszy przycisk
            } else if (changeType === 'strategic' && buttons[1]) {
                targetButton = buttons[1]; // drugi przycisk
            }

            if (!targetButton) return;
            // Symulujemy kliknięcie przycisku
            targetButton.click();
        });

        
        setTimeout(() => {
        
            const updatedBoxes = Array.from(document.querySelectorAll('#priceContainer .price-box'));
            let countAdded = 0;
            let countRejected = 0;
            updatedBoxes.forEach(box => {
                // Jeśli w boxie pojawiła się klasa 'price-changed', uznajemy, że zmiana została dodana
                if (box.classList.contains('price-changed')) {
                    countAdded++;
                } else {
                    countRejected++;
                }
            });

            showGlobalUpdate(
                `<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Masowa zmiana zakończona!</p>
             <p>Dodano: <strong>${countAdded}</strong> SKU</p>
             <p>Odrzucono: <strong>${countRejected}</strong> SKU</p>`
            );
        }, 500); 
    }




    function showGlobalNotification(message) {
        const notif = document.getElementById("globalNotification");
        if (!notif) return;
        notif.innerHTML = message; // używamy innerHTML
        notif.style.display = "block";

        setTimeout(() => {
            notif.style.display = "none";
        }, 7000);
    }

    function showGlobalUpdate(message) {
        const notif = document.getElementById("globalUpdate");
        if (!notif) return;
        notif.innerHTML = message; // używamy innerHTML
        notif.style.display = "block";

        setTimeout(() => {
            notif.style.display = "none";
        }, 7000);
    }



    function exportToExcelXLSX() {
        const workbook = new ExcelJS.Workbook();

        const worksheet = workbook.addWorksheet("Dane");

        worksheet.addRow(["ID", "Nazwa Produktu", "EAN", "Ilość ofert", "Najniższa Konkurencyjna Cena", "Twoja Cena"]);

        const fontRed = { color: { argb: "FFAA0000" } };
        const fontGreen = { color: { argb: "FF006400" } };
        const fontGray = { color: { argb: "FF7E7E7E" } };

        allPrices.forEach((item) => {
            const rowData = [
                item.externalId || "",
                item.productName || "",
                item.ean || "",
                item.storeCount || "",
                item.lowestPrice || "",
                item.myPrice || ""
            ];

            const row = worksheet.addRow(rowData);

            const lowestPriceCell = row.getCell(5);
            const myPriceCell = row.getCell(6);

            const lowest = parseFloat(item.lowestPrice) || 0;
            const mine = parseFloat(item.myPrice) || 0;

            if (lowest > 0 && mine > 0) {
                if (mine < lowest) {
                    lowestPriceCell.font = fontRed;
                    myPriceCell.font = fontGreen;
                } else if (mine > lowest) {
                    lowestPriceCell.font = fontGreen;
                    myPriceCell.font = fontRed;
                } else {
                    lowestPriceCell.font = fontGreen;
                    myPriceCell.font = fontGreen;
                }
            } else {
                lowestPriceCell.font = fontGray;
                myPriceCell.font = fontGray;
            }
        });

        workbook.xlsx.writeBuffer().then((buffer) => {
            const blob = new Blob([buffer], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
            const fileName = `PriceSafari-${scrapDateJS}-${myStoreNameJS}.xlsx`;

            const link = document.createElement("a");
            link.href = URL.createObjectURL(blob);
            link.download = fileName;

            link.click();

            setTimeout(() => {
                URL.revokeObjectURL(link.href);
            }, 1000);
        });
    }

    let currentPage = 1;
    const itemsPerPage = 1000;

    function resetPage() {
        currentPage = 1;
        window.scrollTo(0, 0);
    }

    function renderPaginationControls(totalItems) {
        const totalPages = Math.ceil(totalItems / itemsPerPage);
        const paginationContainer = document.getElementById('paginationContainer');
        paginationContainer.innerHTML = '';

        const prevButton = document.createElement('button');
        prevButton.textContent = 'Poprzednia';
        prevButton.disabled = currentPage === 1;
        prevButton.addEventListener('click', () => {
            if (currentPage > 1) {
                currentPage--;
                filterPricesAndUpdateUI(false);
            }
        });

        paginationContainer.appendChild(prevButton);

        for (let i = 1; i <= totalPages; i++) {
            const pageButton = document.createElement('button');
            pageButton.textContent = i;
            if (i === currentPage) {
                pageButton.classList.add('active');
            }
            pageButton.addEventListener('click', () => {
                currentPage = i;
                filterPricesAndUpdateUI(false);
            });
            paginationContainer.appendChild(pageButton);
        }

        const nextButton = document.createElement('button');
        nextButton.textContent = 'Następna';
        nextButton.disabled = currentPage === totalPages;
        nextButton.addEventListener('click', () => {
            if (currentPage < totalPages) {
                currentPage++;
                filterPricesAndUpdateUI(false);
            }
        });

        paginationContainer.appendChild(nextButton);
    }

    function debounce(func, wait) {
        let timeout;
        return function (...args) {
            const context = this;
            clearTimeout(timeout);
            timeout = setTimeout(() => func.apply(context, args), wait);
        };
    }

    positionSlider = document.getElementById('positionRangeSlider');
    var positionRangeInput = document.getElementById('positionRange');

    noUiSlider.create(positionSlider, {
        start: [1, 60],
        connect: true,
        range: {
            'min': 1,
            'max': 60
        },
        step: 1,
        format: wNumb({
            decimals: 0
        })
    });

    function enforceLimits(input, min, max) {
        let value = parseFloat(input.value.replace(',', '.'));
        if (isNaN(value)) {
            return;
        }
        if (value < min) {
            input.value = min.toFixed(2);
        } else if (value > max) {
            input.value = max.toFixed(2);
        } else {
            input.value = value.toFixed(2);
        }
    }

    const price1Input = document.getElementById("price1");
    const price2Input = document.getElementById("price2");
    const stepPriceInput = document.getElementById("stepPrice");

    price1Input.addEventListener("blur", () => {
        enforceLimits(price1Input, 0.01, 100);
        if (parseFloat(stepPriceInput.value.replace(',', '.')) > parseFloat(price1Input.value.replace(',', '.'))) {
            stepPriceInput.value = price1Input.value;
        }
        enforceLimits(stepPriceInput, 0.01, parseFloat(price1Input.value.replace(',', '.')));
    });

    price2Input.addEventListener("blur", () => {
        enforceLimits(price2Input, 0.01, 100);
    });

    stepPriceInput.addEventListener("blur", () => {
        enforceLimits(stepPriceInput, 0.01, parseFloat(price1Input.value.replace(',', '.')));
    });

    positionSlider.noUiSlider.on('update', function (values, handle) {
        const displayValues = values.map(value => {
            return parseInt(value) === 60 ? 'Schowany' : 'Pozycja ' + value;
        });
        positionRangeInput.textContent = displayValues.join(' - ');
    });

    positionSlider.noUiSlider.on('change', function () {
        filterPricesAndUpdateUI();
    });

    offerSlider = document.getElementById('offerRangeSlider');
    var offerRangeInput = document.getElementById('offerRange');

    noUiSlider.create(offerSlider, {
        start: [1, 1],
        connect: true,
        range: {
            'min': 1,
            'max': 1
        },
        step: 1,
        format: wNumb({
            decimals: 0
        })
    });

    offerSlider.noUiSlider.on('update', function (values, handle) {
        const displayValues = values.map(value => {
            const intValue = parseInt(value);
            let suffix = ' Ofert';
            if (intValue === 1) suffix = ' Oferta';
            else if (intValue >= 2 && intValue <= 4) suffix = ' Oferty';
            return intValue + suffix;
        });
        offerRangeInput.textContent = displayValues.join(' - ');
    });

    offerSlider.noUiSlider.on('change', function () {
        filterPricesAndUpdateUI();
    });

    function convertPriceValue(price, usePriceDifference) {
        if (price.onlyMe) {
            return { valueToUse: null, colorClass: 'prOnlyMe' };
        } else {
            let valueToUse = usePriceDifference
                ? (price.savings !== null ? price.savings : price.priceDifference)
                : price.percentageDifference;

            if (valueToUse !== null && valueToUse !== undefined) {
                valueToUse = parseFloat(valueToUse.toFixed(2));
            }
            const colorClass = getColorClass(valueToUse, price.isUniqueBestPrice, price.isSharedBestPrice);
            return { valueToUse, colorClass };
        }
    }

    const updatePricesDebounced = debounce(function () {
        const usePriceDifference = document.getElementById('usePriceDifference').checked;

        allPrices.forEach(price => {
            const result = convertPriceValue(price, usePriceDifference);
            price.valueToUse = result.valueToUse;
            price.colorClass = result.colorClass;
        });

        filterPricesAndUpdateUI();
    }, 300);

    function loadStores() {
        fetch(`/PriceHistory/GetStores?storeId=${storeId}`)
            .then(response => response.json())
            .then(stores => {
                const storeSelect = document.getElementById('competitorStoreSelect');
                stores.forEach(store => {
                    const option = document.createElement('option');
                    option.value = store;
                    option.text = store;
                    storeSelect.appendChild(option);
                });

                storeSelect.addEventListener('change', function () {
                    competitorStore = this.value;
                    loadPrices();
                });
            })
            .catch(error => console.error('Error fetching stores:', error));
    }

    const usePriceDifferenceCheckbox = document.getElementById('usePriceDifference');

    const unitLabel1 = document.getElementById('unitLabel1');
    const unitLabel2 = document.getElementById('unitLabel2');
    const unitLabelStepPrice = document.getElementById('unitLabelStepPrice');

    function updateUnits(usePriceDifference) {
        if (usePriceDifference) {
            unitLabel1.textContent = 'PLN';
            unitLabel2.textContent = 'PLN';
            unitLabelStepPrice.textContent = 'PLN';
        } else {
            unitLabel1.textContent = '%';
            unitLabel2.textContent = '%';
            unitLabelStepPrice.textContent = '%';
        }
    }

    usePriceDifferenceCheckbox.addEventListener('change', function () {
        usePriceDifference = this.checked;
        updateUnits(usePriceDifference);
    });



    function loadPrices() {
        showLoading();

        const source = document.getElementById('sourceSelect').value;
        fetch(`/PriceHistory/GetPrices?storeId=${storeId}&competitorStore=${encodeURIComponent(competitorStore)}&source=${encodeURIComponent(source)}`)
            .then(response => response.json())
            .then(response => {
                myStoreName = response.myStoreName;
                setPrice1 = response.setPrice1;
                setPrice2 = response.setPrice2;
                setStepPrice = response.stepPrice;
                missedProductsCount = response.missedProductsCount;

                usePriceDifference = response.usePriceDiff;
                document.getElementById('usePriceDifference').checked = usePriceDifference;
                updateUnits(usePriceDifference);

                // Ustawienia marży pobrane z backendu:
                marginSettings.useMarginForSimulation = response.useMarginForSimulation;
                marginSettings.enforceMinimalMargin = response.enforceMinimalMargin;
                marginSettings.minimalMarginPercent = response.minimalMarginPercent;

                allPrices = response.prices.map(price => {
                    const isRejected = price.isRejected;
                    const onlyMe = price.onlyMe === true;
                    let valueToUse = null;
                    let colorClass = '';

                    if (onlyMe) {
                        colorClass = 'prOnlyMe';
                    } else if (!isRejected) {
                        if (usePriceDifference) {
                            valueToUse = price.savings !== null ? price.savings : price.priceDifference;
                        } else {
                            valueToUse = price.percentageDifference;
                        }
                        colorClass = getColorClass(valueToUse, price.isUniqueBestPrice, price.isSharedBestPrice);
                    } else {
                        colorClass = 'prRejected';
                    }

                    const marginPrice = price.marginPrice != null && !isNaN(price.marginPrice) ? parseFloat(price.marginPrice) : null;
                    const myPrice = price.myPrice != null && !isNaN(price.myPrice) ? parseFloat(price.myPrice) : null;
                    let marginAmount = null;
                    let marginPercentage = null;
                    let marginSign = '';
                    let marginClass = '';

                    if (!isRejected && marginPrice != null && myPrice != null) {
                        marginAmount = myPrice - marginPrice;
                        if (marginPrice !== 0) {
                            marginPercentage = (marginAmount / marginPrice) * 100;
                        } else {
                            marginPercentage = null;
                        }

                        marginSign = marginAmount >= 0 ? '+' : '-';
                        marginClass = marginAmount >= 0 ? 'priceBox-diff-margin' : 'priceBox-diff-margin-minus';
                    }

                    return {
                        ...price,
                        isRejected: price.isRejected || false,
                        onlyMe: onlyMe,
                        valueToUse: onlyMe ? null : valueToUse,
                        colorClass: colorClass,
                        marginPrice: marginPrice,
                        myPrice: myPrice,
                        marginAmount: marginAmount,
                        marginPercentage: marginPercentage,
                        marginSign: marginSign,
                        marginClass: marginClass
                    };
                });

                const storeCounts = allPrices.map(item => item.storeCount);
                const maxStoreCount = Math.max(...storeCounts);
                const offerSliderMax = Math.max(maxStoreCount, 1);

                offerSlider.noUiSlider.updateOptions({
                    range: {
                        'min': 1,
                        'max': offerSliderMax
                    }
                });

                offerSlider.noUiSlider.set([1, offerSliderMax]);

                document.getElementById('totalPriceCount').textContent = response.priceCount;
                document.getElementById('price1').value = setPrice1;
                document.getElementById('price2').value = setPrice2;
                document.getElementById('stepPrice').value = setStepPrice;
                document.getElementById('missedProductsCount').textContent = missedProductsCount;

                updateFlagCounts(allPrices);
                const currentSearchTerm = document.getElementById('productSearch').value;
                let filteredPrices = allPrices;

                if (currentSearchTerm) {
                    filteredPrices = filteredPrices.filter(price => {
                        // 1. Oczyszczamy "currentSearchTerm" do postaci bez spacji itp.
                        const sanitizedInput = currentSearchTerm.replace(/[^a-zA-Z0-9\s.-]/g, '').trim().toLowerCase().replace(/\s+/g, '');

                        // 2. Łączymy productName + ean w jeden string
                        const productNamePlusEan = (price.productName || '') + ' ' + (price.ean || '');

                        // 3. Także oczyszczamy
                        const combinedLower = productNamePlusEan
                            .toLowerCase()
                            .replace(/[^a-zA-Z0-9\s/.-]/g, '')
                            .replace(/\s+/g, '');

                        // 4. Sprawdzamy, czy combinedLower zawiera sanitizedInput
                        return combinedLower.includes(sanitizedInput);
                    });
                }

                filteredPrices = filterPricesByCategoryAndColorAndFlag(filteredPrices);

                renderPrices(filteredPrices);
                debouncedRenderChart(filteredPrices);
                updateColorCounts(filteredPrices);
                updateMarginSortButtonsVisibility();
            })

            .finally(() => {
                hideLoading();
            });

             window.loadPrices = loadPrices;

    }

    function updateFlagCounts(prices) {
        const flagCounts = {};
        let noFlagCount = 0;

        prices.forEach(price => {
            if (price.flagIds.length === 0) {
                noFlagCount++;
            }
            price.flagIds.forEach(flagId => {
                if (!flagCounts[flagId]) {
                    flagCounts[flagId] = 0;
                }
                flagCounts[flagId]++;
            });
        });

        const flagContainer = document.getElementById('flagContainer');
        flagContainer.innerHTML = '';

        flags.forEach(flag => {
            const count = flagCounts[flag.FlagId] || 0;
            const flagElement = `
            <div class="form-check">
                <input class="form-check-input flagFilter" type="checkbox" id="flagCheckbox_${flag.FlagId}" value="${flag.FlagId}" ${selectedFlags.has(flag.FlagId.toString()) ? 'checked' : ''}>
                <label class="form-check-label" for="flagCheckbox_${flag.FlagId}">${flag.FlagName} (${count})</label>
            </div>
        `;
            flagContainer.innerHTML += flagElement;
        });

        const noFlagElement = `
        <div class="form-check">
            <input class="form-check-input flagFilter" type="checkbox" id="noFlagCheckbox" value="noFlag" ${selectedFlags.has('noFlag') ? 'checked' : ''}>
            <label class="form-check-label" for="noFlagCheckbox">Brak flagi (${noFlagCount})</label>
        </div>
        `;
        flagContainer.innerHTML += noFlagElement;

        document.querySelectorAll('.flagFilter').forEach(checkbox => {
            checkbox.addEventListener('change', function () {
                const flagValue = this.value;
                if (this.checked) {
                    selectedFlags.add(flagValue);
                } else {
                    selectedFlags.delete(flagValue);
                }
                filterPricesAndUpdateUI();
            });
        });
    }

    function filterPricesByCategoryAndColorAndFlag(data) {
        const selectedColors = Array.from(document.querySelectorAll('.colorFilter:checked')).map(checkbox => checkbox.value);
        const selectedBid = document.getElementById('bidFilter').checked;
        const suspiciouslyLowFilter = document.getElementById('suspiciouslyLowFilter').checked;
        const selectedDeliveryMyStore = Array.from(document.querySelectorAll('.deliveryFilterMyStore:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedDeliveryCompetitor = Array.from(document.querySelectorAll('.deliveryFilterCompetitor:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedExternalPrice = Array.from(document.querySelectorAll('.externalPriceFilter:checked')).map(checkbox => checkbox.value);

        const positionSliderValues = positionSlider.noUiSlider.get();
        const positionMin = parseInt(positionSliderValues[0]);
        const positionMax = parseInt(positionSliderValues[1]);

        const offerSliderValues = offerSlider.noUiSlider.get();
        const offerMin = parseInt(offerSliderValues[0]);
        const offerMax = parseInt(offerSliderValues[1]);

        let filteredPrices = data;

        filteredPrices = filteredPrices.filter(item => {
            const position = item.myPosition;
            if (position === null || position === undefined) {
                return true;
            }

            const numericPosition = parseInt(position);
            const isInRange = numericPosition >= positionMin && numericPosition <= positionMax;
            if (!isInRange) {
            }
            return isInRange;
        });

        filteredPrices = filteredPrices.filter(item => {
            const storeCount = item.storeCount;
            return storeCount >= offerMin && storeCount <= offerMax;
        });

        if (suspiciouslyLowFilter) {
            // a) zostawiamy TYLKO te oferty, gdzie singleBestCheaperDiffPerc > 25
            filteredPrices = filteredPrices.filter(item =>
                item.singleBestCheaperDiffPerc !== null &&
                item.singleBestCheaperDiffPerc > 25
            );

            // b) sortujemy je malejąco według singleBestCheaperDiffPerc
            filteredPrices.sort((a, b) => b.singleBestCheaperDiffPerc - a.singleBestCheaperDiffPerc);
        }

        if (selectedColors.length) {
            filteredPrices = filteredPrices.filter(item => selectedColors.includes(item.colorClass));
        }

        if (selectedFlags.has("noFlag")) {
            filteredPrices = filteredPrices.filter(item => item.flagIds.length === 0);
        } else if (selectedFlags.size > 0) {
            filteredPrices = filteredPrices.filter(item => Array.from(selectedFlags).some(flag => item.flagIds.includes(parseInt(flag))));
        }

        if (selectedBid) {
            filteredPrices = filteredPrices.filter(item => item.myIsBidding === "1");
        }

        if (selectedDeliveryMyStore.length) {
            filteredPrices = filteredPrices.filter(item => selectedDeliveryMyStore.includes(item.myDelivery));
        }

        if (selectedDeliveryCompetitor.length) {
            filteredPrices = filteredPrices.filter(item => selectedDeliveryCompetitor.includes(item.delivery));
        }

        if (selectedExternalPrice.includes("yes")) {
            filteredPrices = filteredPrices.filter(item => item.externalPrice !== null);
        } else if (selectedExternalPrice.includes("no")) {
            filteredPrices = filteredPrices.filter(item => item.externalPrice === null);
        }

        return filteredPrices;
    }

    function getColorClass(valueToUse, isUniqueBestPrice = false, isSharedBestPrice = false) {
        if (isSharedBestPrice) {
            return "prGood";
        }
        if (isUniqueBestPrice) {
            return valueToUse <= setPrice1 ? "prIdeal" : "prToLow";
        }

        return valueToUse < setPrice2 ? "prMid" : "prToHigh";
    }
    function renderPrices(data) {
        // Na początku funkcji odświeżamy dane o zmianach z localStorage
        const storedChanges = localStorage.getItem('selectedPriceChanges_' + storeId);
        if (storedChanges) {
            try {
                selectedPriceChanges = JSON.parse(storedChanges);
            } catch (err) {
                console.error("Błąd parsowania danych z localStorage:", err);
                selectedPriceChanges = [];
            }
        } else {
            selectedPriceChanges = [];
        }

        const container = document.getElementById('priceContainer');
        const currentProductSearchTerm = document.getElementById('productSearch').value.trim();
        const currentStoreSearchTerm = document.getElementById('storeSearch').value.trim();
        container.innerHTML = '';

        // Paginacja – zakładamy, że currentPage i itemsPerPage są globalnie zdefiniowane
        const startIndex = (currentPage - 1) * itemsPerPage;
        const endIndex = currentPage * itemsPerPage;
        const paginatedData = data.slice(startIndex, endIndex);

        // Funkcja, która ustawia przycisk na "Dodano ..." i dodaje możliwość usunięcia zmiany
        function activateChangeButton(button, priceBox, newPrice) {
            button.classList.add('active');
            button.style.backgroundColor = "#333333";
            button.style.color = "#f5f5f5";
            button.textContent = "Dodano |";

            // Dodajemy link do usuwania zmiany
            const removeLink = document.createElement('span');
            removeLink.innerHTML = " <i class='fa fa-trash-o' style='font-size:16px; display:flex; color:white; margin-left:4px; margin-top:2px;'></i>";

            removeLink.style.textDecoration = "none";
            removeLink.style.cursor = "pointer";
            removeLink.addEventListener('click', function (ev) {
                ev.stopPropagation();
                button.classList.remove('active');
                button.style.backgroundColor = "";
                button.style.color = "";
                button.textContent = "Zmień cenę";
                priceBox.classList.remove('price-changed');

                const productId = priceBox ? priceBox.dataset.productId : null;
                // Wywołujemy event usuwania – PriceChange.js odbierze go i zaktualizuje localStorage
                const removeEvent = new CustomEvent('priceBoxChangeRemove', {
                    detail: { productId }
                });
                document.dispatchEvent(removeEvent);
            });
            button.appendChild(removeLink);
            priceBox.classList.add('price-changed');
        }

        function attachPriceChangeListener(button, suggestedPrice, priceBox, productId, productName, currentPriceValue, item) {
            // Sprawdzenie, czy produkt ma zmapowany EAN
            if (!item || !item.ean || item.ean.trim() === "") {
                button.disabled = true;
                button.title = "Produkt musi mieć zmapowany kod EAN";
                return;
            }

            button.addEventListener('click', function (e) {
                e.stopPropagation();

                // Jeśli używamy symulacji z marżą, produkt musi mieć podaną cenę zakupu (marginPrice)
                if (marginSettings.useMarginForSimulation) {
                    if (item.marginPrice == null) {
                        showGlobalNotification(
                            `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                     <p>Symulacja cenowa z marżą jest włączona – produkt musi posiadać cenę zakupu.</p>`
                        );
                        return;
                    }
                    // Obliczamy nową (symulowaną) marżę:
                    // Marża = ((Nowa cena - cena zakupu) / cena zakupu) * 100
                    let simulatedMargin = ((suggestedPrice - item.marginPrice) / item.marginPrice) * 100;
                    simulatedMargin = parseFloat(simulatedMargin.toFixed(2));

                    // Jeśli wymuszamy minimalną (lub maksymalną) marżę...
                    if (marginSettings.enforceMinimalMargin) {
                        if (simulatedMargin < 0) {
                            showGlobalNotification(
                                `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                         <p>Nowa cena <strong>${suggestedPrice.toFixed(2)} PLN</strong> spowoduje ujemną marżę (Nowa marża: <strong>${simulatedMargin}%</strong>).</p>
                         <p>Cena zakupu wynosi <strong>${item.marginPrice.toFixed(2)} PLN</strong>. Zmiana nie może zostać zastosowana.</p>`
                            );
                            return;
                        }
                        if (marginSettings.minimalMarginPercent > 0 && simulatedMargin < marginSettings.minimalMarginPercent) {
                            showGlobalNotification(
                                `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                         <p>Nowa cena <strong>${suggestedPrice.toFixed(2)} PLN</strong> obniży marżę poniżej ustalonego minimum (<strong>${marginSettings.minimalMarginPercent}%</strong>).</p>
                         <p>Nowa marża wynosi <strong>${simulatedMargin}%</strong>, a cena zakupu to <strong>${item.marginPrice.toFixed(2)} PLN</strong>.</p>`
                            );
                            return;
                        }
                        if (marginSettings.minimalMarginPercent < 0 && simulatedMargin > marginSettings.minimalMarginPercent) {
                            showGlobalNotification(
                                `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                         <p>Nowa cena <strong>${suggestedPrice.toFixed(2)} PLN</strong> podniesie marżę powyżej dozwolonego poziomu (<strong>${marginSettings.minimalMarginPercent}%</strong>).</p>
                         <p>Nowa marża wynosi <strong>${simulatedMargin}%</strong>.</p>`
                            );
                            return;
                        }
                    }
                }

                // Jeśli zmiana już jest aktywna – nie wykonujemy nic więcej
                if (priceBox.classList.contains('price-changed') || button.classList.contains('active')) {
                    console.log("Zmiana ceny już aktywna dla produktu", productId);
                    return;
                }

                // Wysyłamy event zmiany ceny
                const priceChangeEvent = new CustomEvent('priceBoxChange', {
                    detail: { productId, productName, currentPrice: currentPriceValue, newPrice: suggestedPrice, storeId: storeId }
                });
                document.dispatchEvent(priceChangeEvent);
                activateChangeButton(button, priceBox, suggestedPrice);

                // Budujemy rozbudowany komunikat sukcesu – informacje zaczynają się od nowej linii, z odstępami
                let message = `<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Zmiana ceny dodana</p>`;
                message += `<p style="margin:4px 0;"><strong>Produkt:</strong> ${productName}</p>`;
                message += `<p style="margin:4px 0;"><strong>Nowa cena:</strong> ${suggestedPrice.toFixed(2)} PLN</p>`;
                if (marginSettings.useMarginForSimulation) {
                    let simulatedMargin = ((suggestedPrice - item.marginPrice) / item.marginPrice) * 100;
                    simulatedMargin = parseFloat(simulatedMargin.toFixed(2));
                    message += `<p style="margin:4px 0;"><strong>Nowa marża:</strong> ${simulatedMargin}%</p>`;
                    message += `<p style="margin:4px 0;"><strong>Cena zakupu:</strong> ${item.marginPrice.toFixed(2)} PLN</p>`;
                    if (marginSettings.enforceMinimalMargin) {
                        if (marginSettings.minimalMarginPercent > 0) {
                            message += `<p style="margin:4px 0;"><strong>Minimalna wymagana marża:</strong> ${marginSettings.minimalMarginPercent}%</p>`;
                        } else if (marginSettings.minimalMarginPercent < 0) {
                            message += `<p style="margin:4px 0;"><strong>Maksymalne obniżenie marży:</strong> ${marginSettings.minimalMarginPercent}%</p>`;
                        }
                    }
                }
                showGlobalUpdate(message);
            });

            // Sprawdzamy, czy zmiana już istnieje (np. zapisana w localStorage) i aktywujemy ją, jeśli tak
            const existingChange = selectedPriceChanges.find(change =>
                parseInt(change.productId) === parseInt(productId) &&
                parseFloat(change.newPrice) === parseFloat(suggestedPrice)
            );
            if (existingChange) {
                activateChangeButton(button, priceBox, suggestedPrice);
            }
        }






        paginatedData.forEach(item => {
            const isRejected = item.isRejected;
            const highlightedProductName = highlightMatches(item.productName, currentProductSearchTerm);
            const highlightedStoreName = highlightMatches(item.storeName, currentStoreSearchTerm);
            const isBidding = item.isBidding === "1";
            const deliveryClass = getDeliveryClass(item.delivery);

            let percentageDifference = null;
            let priceDifference = null;
            let savings = null;
            let myIsBidding = null;
            let myDeliveryClass = null;
            let marginAmount = null;
            let marginPercentage = null;
            let marginSign = '';
            let marginClass = '';
            let marginPrice = null;
            let myPrice = null;
            let myPosition = null;
            let lowestPrice = null;

            if (!isRejected) {
                percentageDifference = item.percentageDifference != null ? item.percentageDifference.toFixed(2) : "N/A";
                priceDifference = item.priceDifference != null ? item.priceDifference.toFixed(2) : "N/A";
                savings = item.savings != null ? item.savings.toFixed(2) : "N/A";
                myIsBidding = item.myIsBidding === "1";
                myDeliveryClass = getDeliveryClass(item.myDelivery);
                marginAmount = item.marginAmount;
                marginPercentage = item.marginPercentage;
                marginSign = item.marginSign;
                marginClass = item.marginClass;
                marginPrice = item.marginPrice;
                myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
                myPosition = item.myPosition;
                lowestPrice = item.lowestPrice != null ? parseFloat(item.lowestPrice) : null;
            }

            // Główny kontener produktu
            const box = document.createElement('div');
            box.className = 'price-box ' + item.colorClass;
            box.dataset.detailsUrl = '/PriceHistory/Details?scrapId=' + item.scrapId + '&productId=' + item.productId;
            box.dataset.productId = item.productId;
            box.dataset.productName = item.productName;

            // Kliknięcie w cały box – przejście do szczegółów
            box.addEventListener('click', function () {
                window.open(this.dataset.detailsUrl, '_blank');
            });

            const priceBoxSpace = document.createElement('div');
            priceBoxSpace.className = 'price-box-space';

            const priceBoxColumnName = document.createElement('div');
            priceBoxColumnName.className = 'price-box-column-name';
            priceBoxColumnName.innerHTML = highlightedProductName;

            const priceBoxColumnCategory = document.createElement('div');
            priceBoxColumnCategory.className = 'price-box-column-category';

            // W wersji ulepszonej zamiast API ID wyświetlamy EAN – z dodatkowym podświetleniem
            if (item.externalId) {
                const apiBox = document.createElement('span');
                apiBox.className = 'ApiBox';
                const displayedEan = highlightMatches(item.ean || '', currentProductSearchTerm, 'highlighted-text-yellow');
                apiBox.innerHTML = 'EAN ' + displayedEan;
                priceBoxColumnCategory.appendChild(apiBox);
            }

            const flagsContainer = createFlagsContainer(item);

            const assignFlagButton = document.createElement('button');
            assignFlagButton.className = 'assign-flag-button';
            assignFlagButton.dataset.productId = item.productId;
            assignFlagButton.innerHTML = '+ Przypisz flagi';
            assignFlagButton.style.pointerEvents = 'auto';

            // Obsługa przypisywania flag
            assignFlagButton.addEventListener('click', function (event) {
                event.stopPropagation();
                selectedProductId = this.dataset.productId;
                modal.style.display = 'block';

                fetch(`/ProductFlags/GetFlagsForProduct?productId=${selectedProductId}`)
                    .then(response => response.json())
                    .then(flags => {
                        document.querySelectorAll('.flagCheckbox').forEach(checkbox => {
                            checkbox.checked = flags.includes(parseInt(checkbox.value));
                        });
                    })
                    .catch(error => console.error('Błąd pobierania flag dla produktu:', error));
            });

            priceBoxSpace.appendChild(priceBoxColumnName);
            priceBoxSpace.appendChild(flagsContainer);
            priceBoxSpace.appendChild(assignFlagButton);
            priceBoxSpace.appendChild(priceBoxColumnCategory);

            const externalInfoContainer = document.createElement('div');
            externalInfoContainer.className = 'price-box-externalInfo';

            const priceBoxColumnStoreCount = document.createElement('div');
            priceBoxColumnStoreCount.className = 'price-box-column-offers';
            priceBoxColumnStoreCount.innerHTML =
                ((item.sourceGoogle || item.sourceCeneo) ?
                    '<span class="data-channel">' +
                    (item.sourceGoogle ? '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:15px; height:15px;" />' : '') +
                    (item.sourceCeneo ? '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:15px; height:15px;" />' : '') +
                    '</span>' : ''
                ) +
                '<div class="offer-count-box">' + item.storeCount + ' Ofert</div>';

            externalInfoContainer.appendChild(priceBoxColumnStoreCount);

            // Wyświetlanie ceny zakupu / marży
            if (marginPrice != null) {
                const formattedMarginPrice = marginPrice.toLocaleString('pl-PL', {
                    minimumFractionDigits: 2,
                    maximumFractionDigits: 2
                }) + ' PLN';

                const purchasePriceBox = document.createElement('div');
                purchasePriceBox.className = 'price-box-diff-margin ' + marginClass;
                purchasePriceBox.innerHTML = '<p>Cena zakupu: ' + formattedMarginPrice + '</p>';

                externalInfoContainer.appendChild(purchasePriceBox);

                if (myPrice != null) {
                    const formattedMarginAmount = marginSign + Math.abs(marginAmount).toLocaleString('pl-PL', {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2
                    }) + ' PLN';
                    const formattedMarginPercentage = '(' + marginSign + Math.abs(marginPercentage).toLocaleString('pl-PL', {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2
                    }) + '%)';

                    const marginBox = document.createElement('div');
                    marginBox.className = 'price-box-diff-margin ' + marginClass;
                    marginBox.innerHTML = '<p>Marża: ' + formattedMarginAmount + ' ' + formattedMarginPercentage + '</p>';

                    externalInfoContainer.appendChild(marginBox);
                }
            }

            priceBoxSpace.appendChild(externalInfoContainer);

            const priceBoxData = document.createElement('div');
            priceBoxData.className = 'price-box-data';

            const colorBar = document.createElement('div');
            colorBar.className = 'color-bar ' + item.colorClass;

            const priceBoxColumnLowestPrice = document.createElement('div');
            priceBoxColumnLowestPrice.className = 'price-box-column';

            const priceBoxLowestText = document.createElement('div');
            priceBoxLowestText.className = 'price-box-column-text';

            const priceLine = document.createElement('div');
            priceLine.style.display = 'flex';

            const priceSpan = document.createElement('div');
            priceSpan.style.fontWeight = '500';
            priceSpan.style.fontSize = '17px';
            priceSpan.textContent = item.lowestPrice.toFixed(2) + ' PLN';
            priceLine.appendChild(priceSpan);

            // ★ TOP
            if (typeof item.externalBestPriceCount !== 'undefined' && item.externalBestPriceCount !== null) {
                if (item.externalBestPriceCount === 1) {
                    const uniqueBox = document.createElement('div');
                    uniqueBox.className = 'uniqueBestPriceBox';
                    uniqueBox.textContent = '★ TOP';
                    uniqueBox.style.marginLeft = '8px';
                    priceLine.appendChild(uniqueBox);
                } else if (item.externalBestPriceCount > 1) {
                    const shareBox = document.createElement('div');
                    shareBox.className = 'shareBestPriceBox';
                    shareBox.textContent = item.externalBestPriceCount + ' TOP';
                    shareBox.style.marginLeft = '8px';
                    priceLine.appendChild(shareBox);
                }
            }
            // singleBestCheaperDiffPerc
            if (item.singleBestCheaperDiffPerc !== null && item.singleBestCheaperDiffPerc !== undefined) {
                const diffBox = document.createElement('div');
                diffBox.style.marginLeft = '4px';
                diffBox.className = item.singleBestCheaperDiffPerc > 25 ? 'singlePriceDiffBoxHigh' : 'singlePriceDiffBox';
                const spanDiff = document.createElement('span');
                spanDiff.textContent = item.singleBestCheaperDiffPerc.toFixed(2) + '%';
                diffBox.appendChild(spanDiff);
                priceLine.appendChild(diffBox);
            }

            priceBoxLowestText.appendChild(priceLine);

            // Nazwa sklepu (lowest)
            const storeNameDiv = document.createElement('div');
            storeNameDiv.innerHTML = highlightedStoreName;
            priceBoxLowestText.appendChild(storeNameDiv);

            const priceBoxLowestDetails = document.createElement('div');
            priceBoxLowestDetails.className = 'price-box-column-text';
            priceBoxLowestDetails.innerHTML =
                (item.isGoogle != null ?
                    '<span class="data-channel"><img src="' +
                    (item.isGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png') +
                    '" alt="Channel Icon" style="width:20px; height:20px; margin-right:4px;" /></div>' : ''
                ) +
                (item.position !== null ?
                    (item.isGoogle ?
                        '<span class="Position-Google">Poz. Google ' + item.position + '</span>' :
                        '<span class="Position">Poz. Ceneo ' + item.position + '</span>'
                    ) :
                    '<span class="Position" style="background-color: #414141;">Schowany</span>'
                ) +
                (isBidding ? '<span class="Bidding">Bid</span>' : '') +
                (item.delivery != null ? '<span class="' + deliveryClass + '">Wysyłka w ' + (item.delivery == 1 ? '1 dzień' : item.delivery + ' dni') + '</span>' : '');

            priceBoxColumnLowestPrice.appendChild(priceBoxLowestText);
            priceBoxColumnLowestPrice.appendChild(priceBoxLowestDetails);

            // Nasza cena
            const priceBoxColumnMyPrice = document.createElement('div');
            priceBoxColumnMyPrice.className = 'price-box-column';

            if (!isRejected && myPrice != null) {
                const priceBoxMyText = document.createElement('div');
                priceBoxMyText.className = 'price-box-column-text';

                if (item.externalPrice !== null) {
                    const externalPriceDifference = (item.externalPrice - myPrice).toFixed(2);
                    const isPriceDecrease = item.externalPrice < myPrice;

                    const priceChangeContainer = document.createElement('div');
                    priceChangeContainer.style.display = 'flex';
                    priceChangeContainer.style.justifyContent = 'space-between';
                    priceChangeContainer.style.alignItems = 'center';

                    const storeName = document.createElement('span');
                    storeName.style.fontWeight = '500';
                    storeName.style.marginRight = '20px';
                    storeName.textContent = myStoreName;

                    const priceDifferenceElem = document.createElement('span');
                    priceDifferenceElem.style.fontWeight = '500';
                    const arrow = '<span class="' + (isPriceDecrease ? 'arrow-down' : 'arrow-up') + '"></span>';
                    priceDifferenceElem.innerHTML = arrow + (isPriceDecrease ? '-' : '+') + Math.abs(externalPriceDifference) + ' PLN ';

                    priceChangeContainer.appendChild(storeName);
                    priceChangeContainer.appendChild(priceDifferenceElem);

                    const priceContainer = document.createElement('div');
                    priceContainer.style.display = 'flex';
                    priceContainer.style.justifyContent = 'space-between';
                    priceContainer.style.alignItems = 'center';

                    const oldPrice = document.createElement('span');
                    oldPrice.style.fontWeight = '500';
                    oldPrice.style.textDecoration = 'line-through';
                    oldPrice.style.marginRight = '10px';
                    oldPrice.textContent = myPrice.toFixed(2) + ' PLN';

                    const newPrice = document.createElement('span');
                    newPrice.style.fontWeight = '500';
                    newPrice.textContent = item.externalPrice.toFixed(2) + ' PLN';

                    priceContainer.appendChild(oldPrice);
                    priceContainer.appendChild(newPrice);

                    priceBoxMyText.appendChild(priceContainer);
                    priceBoxMyText.appendChild(priceChangeContainer);
                } else {
                    priceBoxMyText.innerHTML =
                        '<span style="font-weight: 500; font-size:17px;">' + myPrice.toFixed(2) + ' PLN</span><br>' + myStoreName;
                }

                const priceBoxMyDetails = document.createElement('div');
                priceBoxMyDetails.className = 'price-box-column-text';
                priceBoxMyDetails.innerHTML =
                    (item.myIsGoogle != null ?
                        '<span class="data-channel"><img src="' +
                        (item.myIsGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png') +
                        '" alt="Channel Icon" style="width:20px; height:20px; margin-right:4px;" /></div>' : ''
                    ) +
                    (myPosition !== null ?
                        (item.myIsGoogle ?
                            '<span class="Position-Google">Poz. Google ' + myPosition + '</span>' :
                            '<span class="Position">Poz. Ceneo ' + myPosition + '</span>'
                        ) :
                        '<span class="Position" style="background-color: #414141;">Schowany</span>'
                    ) +
                    (myIsBidding ? '<span class="Bidding">Bid</span>' : '') +
                    (item.myDelivery != null ? '<span class="' + myDeliveryClass + '">Wysyłka w ' + (item.myDelivery == 1 ? '1 dzień' : item.myDelivery + ' dni') + '</span>' : '');

                priceBoxColumnMyPrice.appendChild(priceBoxMyText);
                priceBoxColumnMyPrice.appendChild(priceBoxMyDetails);
            } else {
                const priceBoxMyText = document.createElement('div');
                priceBoxMyText.className = 'price-box-column-text';
                priceBoxMyText.innerHTML = '<span style="font-weight: 500;">Brak ceny</span><br>' + myStoreName;
                priceBoxColumnMyPrice.appendChild(priceBoxMyText);
            }

            const priceBoxColumnInfo = document.createElement('div');
            priceBoxColumnInfo.className = 'price-box-column-action';
            priceBoxColumnInfo.innerHTML = '';



            // ------------------------------
            // Zachowujemy starą logikę linii
            // ------------------------------
            if (!isRejected) {
                // SOLO
                if (item.colorClass === "prOnlyMe") {
                    const diffClass = item.colorClass + ' ' + 'priceBox-diff-top';
                    priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Brak ofert konkurencji</div>';

                    // ZANIŻONA / IDEALNA
                } else if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
                    if (myPrice != null && savings != null) {
                        const savingsValue = parseFloat(savings.replace(',', '.'));
                        const upArrowClass = item.colorClass === 'prToLow' ? 'arrow-up-black' : 'arrow-up-turquoise';

                        const suggestedPrice1 = myPrice + savingsValue;
                        const amountToSuggestedPrice1 = savingsValue;
                        const percentageToSuggestedPrice1 = (amountToSuggestedPrice1 / myPrice) * 100;

                        let suggestedPrice2, amountToSuggestedPrice2, percentageToSuggestedPrice2;
                        let arrowClass2 = upArrowClass;

                        if (usePriceDifference) {
                            if (savingsValue < 1) {
                                suggestedPrice2 = suggestedPrice1 - setStepPrice;
                                amountToSuggestedPrice2 = suggestedPrice2 - myPrice;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
                                if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
                            } else {
                                suggestedPrice2 = suggestedPrice1 - setStepPrice;
                                amountToSuggestedPrice2 = amountToSuggestedPrice1 - setStepPrice;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
                                if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
                            }
                        } else {
                            const percentageStep = setStepPrice / 100;
                            if (savingsValue < 1) {
                                suggestedPrice2 = suggestedPrice1 * (1 - percentageStep);
                                amountToSuggestedPrice2 = suggestedPrice2 - myPrice;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
                                if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
                            } else {
                                suggestedPrice2 = suggestedPrice1 * (1 - percentageStep);
                                amountToSuggestedPrice2 = amountToSuggestedPrice1 - (myPrice * percentageStep);
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
                                if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
                            }
                        }

                        const amount1Formatted = (amountToSuggestedPrice1 >= 0 ? '+' : '') + amountToSuggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentage1Formatted = '(' + (percentageToSuggestedPrice1 >= 0 ? '+' : '') + percentageToSuggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPrice1Formatted = suggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                        const amount2Formatted = (amountToSuggestedPrice2 >= 0 ? '+' : '') + amountToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentage2Formatted = '(' + (percentageToSuggestedPrice2 >= 0 ? '+' : '') + percentageToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPrice2Formatted = suggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                        // Pierwsza linia (matchPriceLine)
                        const matchPriceBox = document.createElement('div');
                        matchPriceBox.className = 'price-box-column';

                        const matchPriceLine = document.createElement('div');
                        matchPriceLine.className = 'price-action-line';

                        const upArrow = document.createElement('span');
                        upArrow.className = upArrowClass;
                        const increaseText = document.createElement('span');
                        increaseText.innerHTML = amount1Formatted + ' ' + percentage1Formatted;
                        const newPriceText = document.createElement('div');
                        newPriceText.innerHTML = '= ' + newSuggestedPrice1Formatted;
                        const colorSquare = document.createElement('span');
                        colorSquare.className = 'color-square-green';

                        matchPriceLine.appendChild(upArrow);
                        matchPriceLine.appendChild(increaseText);
                        matchPriceLine.appendChild(newPriceText);
                        matchPriceLine.appendChild(colorSquare);

                        // Dodajemy przycisk "Zmień cenę"
                        const matchPriceBtn = document.createElement('button');
                        matchPriceBtn.className = 'simulate-change-btn';
                        matchPriceBtn.textContent = 'Zmień cenę';
                        if (!item.ean || item.ean.trim() === "") {
                            matchPriceBtn.addEventListener("click", function (e) {
                                e.preventDefault();
                                e.stopPropagation(); // zapobiega propagacji kliknięcia
                                showGlobalNotification("Produkt musi mieć zmapowany kod EAN");
                            });
                        } else {
                            attachPriceChangeListener(matchPriceBtn, suggestedPrice1, box, item.productId, item.productName, myPrice, item);
                        }


                        matchPriceLine.appendChild(matchPriceBtn);

                        matchPriceBox.appendChild(matchPriceLine);

                        // Druga linia (strategicPriceLine)
                        const strategicPriceBox = document.createElement('div');
                        strategicPriceBox.className = 'price-box-column';

                        const strategicPriceLine = document.createElement('div');
                        strategicPriceLine.className = 'price-action-line';

                        const arrow2Elem = document.createElement('span');
                        arrow2Elem.className = arrowClass2;
                        const increaseText2 = document.createElement('span');
                        increaseText2.innerHTML = amount2Formatted + ' ' + percentage2Formatted;
                        const newPriceText2 = document.createElement('div');
                        newPriceText2.innerHTML = '= ' + newSuggestedPrice2Formatted;
                        const colorSquare2 = document.createElement('span');
                        colorSquare2.className = 'color-square-turquoise';

                        strategicPriceLine.appendChild(arrow2Elem);
                        strategicPriceLine.appendChild(increaseText2);
                        strategicPriceLine.appendChild(newPriceText2);
                        strategicPriceLine.appendChild(colorSquare2);

                        // Drugi przycisk "Zmień cenę"
                        const strategicPriceBtn = document.createElement('button');
                        strategicPriceBtn.className = 'simulate-change-btn';
                        strategicPriceBtn.textContent = 'Zmień cenę';
                        if (!item.ean || item.ean.trim() === "") {
                            strategicPriceBtn.addEventListener("click", function (e) {
                                e.preventDefault();
                                e.stopPropagation();
                                showGlobalNotification("Produkt musi mieć zmapowany kod EAN");
                            });
                        } else {
                            attachPriceChangeListener(strategicPriceBtn, suggestedPrice2, box, item.productId, item.productName, myPrice, item);
                        }

                        strategicPriceLine.appendChild(strategicPriceBtn);

                        strategicPriceBox.appendChild(strategicPriceLine);

                        priceBoxColumnInfo.appendChild(matchPriceBox);
                        priceBoxColumnInfo.appendChild(strategicPriceBox);

                    } else {
                        const diffClass = item.colorClass + ' ' + 'priceBox-diff';
                        priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Podnieś: N/A</div>';
                    }

                    // SUBOPTYMALNA
                } else if (item.colorClass === "prMid") {
                    if (myPrice != null && lowestPrice != null) {
                        const amountToMatchLowestPrice = myPrice - lowestPrice;
                        const percentageToMatchLowestPrice = (amountToMatchLowestPrice / myPrice) * 100;

                        let strategicPrice;
                        if (usePriceDifference) {
                            strategicPrice = lowestPrice - setStepPrice;
                        } else {
                            strategicPrice = lowestPrice * (1 - setStepPrice / 100);
                        }
                        const amountToBeatLowestPrice = myPrice - strategicPrice;
                        const percentageToBeatLowestPrice = (amountToBeatLowestPrice / myPrice) * 100;

                        const amountMatchFormatted = amountToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentageMatchFormatted = '(-' + percentageToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPriceMatch = lowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                        const amountBeatFormatted = amountToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentageBeatFormatted = '(-' + percentageToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPriceBeat = strategicPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                        // Pierwsza linia
                        const matchPriceBox = document.createElement('div');
                        matchPriceBox.className = 'price-box-column';

                        const matchPriceLine = document.createElement('div');
                        matchPriceLine.className = 'price-action-line';

                        const downArrow = document.createElement('span');
                        downArrow.className = 'arrow-down-yellow';
                        const reduceText = document.createElement('span');
                        reduceText.innerHTML = '-' + amountMatchFormatted + ' ' + percentageMatchFormatted;
                        const newPriceText = document.createElement('div');
                        newPriceText.innerHTML = '= ' + newSuggestedPriceMatch;
                        const colorSquare = document.createElement('span');
                        colorSquare.className = 'color-square-green';

                        matchPriceLine.appendChild(downArrow);
                        matchPriceLine.appendChild(reduceText);
                        matchPriceLine.appendChild(newPriceText);
                        matchPriceLine.appendChild(colorSquare);

                        // Przycisk "Zmień cenę"
                        const matchPriceBtn = document.createElement('button');
                        matchPriceBtn.className = 'simulate-change-btn';
                        matchPriceBtn.textContent = 'Zmień cenę';
                      
                      

                
                        if (!item.ean || item.ean.trim() === "") {
                            matchPriceBtn.addEventListener("click", function (e) {
                                e.preventDefault();
                                e.stopPropagation(); // zapobiega propagacji kliknięcia
                                showGlobalNotification("Produkt musi mieć zmapowany kod EAN");
                            });
                        } else {
                            attachPriceChangeListener(matchPriceBtn, lowestPrice, box, item.productId, item.productName, myPrice, item);
                        }

                        matchPriceLine.appendChild(matchPriceBtn);

                        matchPriceBox.appendChild(matchPriceLine);

                        // Druga linia
                        const strategicPriceBox = document.createElement('div');
                        strategicPriceBox.className = 'price-box-column';

                        const strategicPriceLine = document.createElement('div');
                        strategicPriceLine.className = 'price-action-line';

                        const downArrow2 = document.createElement('span');
                        downArrow2.className = 'arrow-down-yellow';
                        const reduceText2 = document.createElement('span');
                        reduceText2.innerHTML = '-' + amountBeatFormatted + ' ' + percentageBeatFormatted;
                        const newPriceText2 = document.createElement('div');
                        newPriceText2.innerHTML = '= ' + newSuggestedPriceBeat;
                        const colorSquare2 = document.createElement('span');
                        colorSquare2.className = 'color-square-turquoise';

                        strategicPriceLine.appendChild(downArrow2);
                        strategicPriceLine.appendChild(reduceText2);
                        strategicPriceLine.appendChild(newPriceText2);
                        strategicPriceLine.appendChild(colorSquare2);

                        // Przycisk "Zmień cenę"
                        const strategicPriceBtn = document.createElement('button');
                        strategicPriceBtn.className = 'simulate-change-btn';
                        strategicPriceBtn.textContent = 'Zmień cenę';
                    
                      


                        if (!item.ean || item.ean.trim() === "") {
                            strategicPriceBtn.addEventListener("click", function (e) {
                                e.preventDefault();
                                e.stopPropagation(); // zapobiega propagacji kliknięcia
                                showGlobalNotification("Produkt musi mieć zmapowany kod EAN");
                            });
                        } else {
                            attachPriceChangeListener(strategicPriceBtn, strategicPrice, box, item.productId, item.productName, myPrice, item);
                        }

                        strategicPriceLine.appendChild(strategicPriceBtn);

                        strategicPriceBox.appendChild(strategicPriceLine);

                        priceBoxColumnInfo.appendChild(matchPriceBox);
                        priceBoxColumnInfo.appendChild(strategicPriceBox);

                    } else {
                        const diffClass = item.colorClass + ' ' + 'priceBox-diff';
                        priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Obniż: N/A</div>';
                    }

                    // ZAWYŻONA
                } else if (item.colorClass === "prToHigh") {
                    if (myPrice != null && lowestPrice != null) {
                        const amountToMatchLowestPrice = myPrice - lowestPrice;
                        const percentageToMatchLowestPrice = (amountToMatchLowestPrice / myPrice) * 100;

                        let strategicPrice;
                        if (usePriceDifference) {
                            strategicPrice = lowestPrice - setStepPrice;
                        } else {
                            strategicPrice = lowestPrice * (1 - setStepPrice / 100);
                        }
                        const amountToBeatLowestPrice = myPrice - strategicPrice;
                        const percentageToBeatLowestPrice = (amountToBeatLowestPrice / myPrice) * 100;

                        const amountMatchFormatted = amountToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentageMatchFormatted = '(-' + percentageToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPriceMatch = lowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                        const amountBeatFormatted = amountToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentageBeatFormatted = '(-' + percentageToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPriceBeat = strategicPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                        // Pierwsza linia
                        const matchPriceBox = document.createElement('div');
                        matchPriceBox.className = 'price-box-column';

                        const matchPriceLine = document.createElement('div');
                        matchPriceLine.className = 'price-action-line';

                        const downArrow = document.createElement('span');
                        downArrow.className = 'arrow-down-red';
                        const reduceText = document.createElement('span');
                        reduceText.innerHTML = '-' + amountMatchFormatted + ' ' + percentageMatchFormatted;
                        const newPriceText = document.createElement('div');
                        newPriceText.innerHTML = '= ' + newSuggestedPriceMatch;
                        const colorSquare = document.createElement('span');
                        colorSquare.className = 'color-square-green';

                        matchPriceLine.appendChild(downArrow);
                        matchPriceLine.appendChild(reduceText);
                        matchPriceLine.appendChild(newPriceText);
                        matchPriceLine.appendChild(colorSquare);

                        // Przycisk "Zmień cenę"
                        const matchPriceBtn = document.createElement('button');
                        matchPriceBtn.className = 'simulate-change-btn';
                        matchPriceBtn.textContent = 'Zmień cenę';
                      
                  

                        if (!item.ean || item.ean.trim() === "") {
                            matchPriceBtn.addEventListener("click", function (e) {
                                e.preventDefault();
                                e.stopPropagation(); // zapobiega propagacji kliknięcia
                                showGlobalNotification("Produkt musi mieć zmapowany kod EAN");
                            });
                        } else {
                            attachPriceChangeListener(matchPriceBtn, lowestPrice, box, item.productId, item.productName, myPrice, item);
                        }


                        matchPriceLine.appendChild(matchPriceBtn);

                        matchPriceBox.appendChild(matchPriceLine);

                        // Druga linia
                        const strategicPriceBox = document.createElement('div');
                        strategicPriceBox.className = 'price-box-column';

                        const strategicPriceLine = document.createElement('div');
                        strategicPriceLine.className = 'price-action-line';

                        const downArrow2 = document.createElement('span');
                        downArrow2.className = 'arrow-down-red';
                        const reduceText2 = document.createElement('span');
                        reduceText2.innerHTML = '-' + amountBeatFormatted + ' ' + percentageBeatFormatted;
                        const newPriceText2 = document.createElement('div');
                        newPriceText2.innerHTML = '= ' + newSuggestedPriceBeat;
                        const colorSquare2 = document.createElement('span');
                        colorSquare2.className = 'color-square-turquoise';

                        strategicPriceLine.appendChild(downArrow2);
                        strategicPriceLine.appendChild(reduceText2);
                        strategicPriceLine.appendChild(newPriceText2);
                        strategicPriceLine.appendChild(colorSquare2);

                        // Przycisk "Zmień cenę"
                        const strategicPriceBtn = document.createElement('button');
                        strategicPriceBtn.className = 'simulate-change-btn';
                        strategicPriceBtn.textContent = 'Zmień cenę';
                   




                        if (!item.ean || item.ean.trim() === "") {
                            strategicPriceBtn.addEventListener("click", function (e) {
                                e.preventDefault();
                                e.stopPropagation(); // zapobiega propagacji kliknięcia
                                showGlobalNotification("Produkt musi mieć zmapowany kod EAN");
                            });
                        } else {
                            attachPriceChangeListener(strategicPriceBtn, strategicPrice, box, item.productId, item.productName, myPrice, item);
                        }


                        strategicPriceLine.appendChild(strategicPriceBtn);

                        strategicPriceBox.appendChild(strategicPriceLine);

                        priceBoxColumnInfo.appendChild(matchPriceBox);
                        priceBoxColumnInfo.appendChild(strategicPriceBox);

                    } else {
                        const diffClass = item.colorClass + ' ' + 'priceBox-diff';
                        priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Obniż: N/A</div>';
                    }

                    // KONKURENCYJNA
                } else if (item.colorClass === "prGood") {
                    if (myPrice != null) {
                        const amountToSuggestedPrice1 = 0;
                        const percentageToSuggestedPrice1 = 0;
                        const suggestedPrice1 = myPrice;

                        const amount1Formatted = '+0,00 PLN';
                        const percentage1Formatted = '(+0,00%)';
                        const newSuggestedPrice1Formatted = '= ' + suggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                        let amountToSuggestedPrice2, percentageToSuggestedPrice2, suggestedPrice2;
                        let amount2Formatted, percentage2Formatted, newSuggestedPrice2Formatted;
                        let downArrowClass, colorSquare2Class;

                        if (item.storeCount === 1) {
                            amountToSuggestedPrice2 = 0;
                            percentageToSuggestedPrice2 = 0;
                            suggestedPrice2 = myPrice;

                            amount2Formatted = '+0,00 PLN';
                            percentage2Formatted = '(+0,00%)';
                            newSuggestedPrice2Formatted = '= ' + suggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                            downArrowClass = 'no-change-icon-turquoise';
                            colorSquare2Class = 'color-square-turquoise';
                        } else {
                            if (usePriceDifference) {
                                amountToSuggestedPrice2 = -setStepPrice;
                                suggestedPrice2 = myPrice + amountToSuggestedPrice2;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
                            } else {
                                const percentageReduction = setStepPrice / 100;
                                amountToSuggestedPrice2 = -myPrice * percentageReduction;
                                if (Math.abs(amountToSuggestedPrice2) < 0.01) {
                                    amountToSuggestedPrice2 = -0.01;
                                }
                                suggestedPrice2 = myPrice + amountToSuggestedPrice2;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
                            }

                            amount2Formatted = (amountToSuggestedPrice2 >= 0 ? '+' : '') + amountToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                            percentage2Formatted = '(' + (percentageToSuggestedPrice2 >= 0 ? '+' : '') + percentageToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                            newSuggestedPrice2Formatted = '= ' + suggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                            downArrowClass = 'arrow-down-green';
                            colorSquare2Class = 'color-square-turquoise';
                        }

                        // Pierwsza linia
                        const matchPriceBox = document.createElement('div');
                        matchPriceBox.className = 'price-box-column';

                        const matchPriceLine = document.createElement('div');
                        matchPriceLine.className = 'price-action-line';

                        const noChangeIcon = document.createElement('span');
                        noChangeIcon.className = 'no-change-icon';
                        const noChangeText = document.createElement('span');
                        noChangeText.innerHTML = amount1Formatted + ' ' + percentage1Formatted;
                        const newPriceText = document.createElement('div');
                        newPriceText.innerHTML = newSuggestedPrice1Formatted;
                        const colorSquare = document.createElement('span');
                        colorSquare.className = 'color-square-green';

                        matchPriceLine.appendChild(noChangeIcon);
                        matchPriceLine.appendChild(noChangeText);
                        matchPriceLine.appendChild(newPriceText);
                        matchPriceLine.appendChild(colorSquare);

                        // Przycisk "Zmień cenę"
                        const matchPriceBtn = document.createElement('button');
                        matchPriceBtn.className = 'simulate-change-btn';
                        matchPriceBtn.textContent = 'Zmień cenę';
                  
                    

                        if (!item.ean || item.ean.trim() === "") {
                            matchPriceBtn.addEventListener("click", function (e) {
                                e.preventDefault();
                                e.stopPropagation(); // zapobiega propagacji kliknięcia
                                showGlobalNotification("Produkt musi mieć zmapowany kod EAN");
                            });
                        } else {
                            attachPriceChangeListener(matchPriceBtn, suggestedPrice1, box, item.productId, item.productName, myPrice, item);
                        }
                        matchPriceLine.appendChild(matchPriceBtn);

                        matchPriceBox.appendChild(matchPriceLine);

                        // Druga linia
                        const strategicPriceBox = document.createElement('div');
                        strategicPriceBox.className = 'price-box-column';

                        const strategicPriceLine = document.createElement('div');
                        strategicPriceLine.className = 'price-action-line';

                        const downArrow = document.createElement('span');
                        downArrow.className = downArrowClass;
                        const reduceText = document.createElement('span');
                        reduceText.innerHTML = amount2Formatted + ' ' + percentage2Formatted;
                        const newPriceText2 = document.createElement('div');
                        newPriceText2.innerHTML = newSuggestedPrice2Formatted;
                        const colorSquare2 = document.createElement('span');
                        colorSquare2.className = colorSquare2Class;

                        strategicPriceLine.appendChild(downArrow);
                        strategicPriceLine.appendChild(reduceText);
                        strategicPriceLine.appendChild(newPriceText2);
                        strategicPriceLine.appendChild(colorSquare2);

                        // Przycisk "Zmień cenę"
                        const strategicPriceBtn = document.createElement('button');
                        strategicPriceBtn.className = 'simulate-change-btn';
                        strategicPriceBtn.textContent = 'Zmień cenę';
                  

                        if (!item.ean || item.ean.trim() === "") {
                            strategicPriceBtn.addEventListener("click", function (e) {
                                e.preventDefault();
                                e.stopPropagation(); // zapobiega propagacji kliknięcia
                                showGlobalNotification("Produkt musi mieć zmapowany kod EAN");
                            });
                        } else {
                            attachPriceChangeListener(strategicPriceBtn, suggestedPrice2, box, item.productId, item.productName, myPrice, item);
                        }
                        strategicPriceLine.appendChild(strategicPriceBtn);

                        strategicPriceBox.appendChild(strategicPriceLine);

                        priceBoxColumnInfo.appendChild(matchPriceBox);
                        priceBoxColumnInfo.appendChild(strategicPriceBox);

                    } else {
                        const diffClass = item.colorClass + ' ' + 'priceBox-diff-top';
                        priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Jesteś w najlepszych cenach</div>';
                    }

                } else {
                    // Produkt odrzucony
                    priceBoxColumnInfo.innerHTML += '<div class="rejected-product">Produkt odrzucony</div>';
                }

            } else {
                priceBoxColumnInfo.innerHTML += '<div class="rejected-product">Produkt odrzucony</div>';
            }

            // Dodajemy colorBar i ewentualnie obrazek
            priceBoxData.appendChild(colorBar);

            if (item.imgUrl) {
                const productImage = document.createElement('img');
                productImage.dataset.src = item.imgUrl;
                productImage.alt = item.productName;
                productImage.className = 'lazy-load';
                productImage.style.width = '110px';
                productImage.style.height = '110px';
                productImage.style.marginRight = '5px';
                productImage.style.marginLeft = '5px';
                productImage.style.backgroundColor = '#ffffff';
                productImage.style.border = '1px solid #e3e3e3';
                productImage.style.borderRadius = '4px';
                productImage.style.padding = '10px';
                productImage.style.display = 'block';

                priceBoxData.appendChild(productImage);
            }

            priceBoxData.appendChild(priceBoxColumnLowestPrice);
            priceBoxData.appendChild(priceBoxColumnMyPrice);
            priceBoxData.appendChild(priceBoxColumnInfo);

            box.appendChild(priceBoxSpace);
            box.appendChild(priceBoxColumnCategory);
            box.appendChild(externalInfoContainer);
            box.appendChild(priceBoxData);

            container.appendChild(box);
        });

        renderPaginationControls(data.length);

        // Lazy-load obrazków
        const lazyLoadImages = document.querySelectorAll('.lazy-load');
        const timers = new Map();

        const observer = new IntersectionObserver((entries, observer) => {
            entries.forEach(entry => {
                const img = entry.target;
                const index = [...lazyLoadImages].indexOf(img);

                if (entry.isIntersecting) {
                    const timer = setTimeout(() => {
                        loadImageWithNeighbors(index);
                        observer.unobserve(img);
                        timers.delete(img);
                    }, 100);
                    timers.set(img, timer);
                } else {
                    if (timers.has(img)) {
                        clearTimeout(timers.get(img));
                        timers.delete(img);
                    }
                }
            });
        }, {
            root: null,
            rootMargin: '50px',
            threshold: 0.01
        });

        function loadImageWithNeighbors(index) {
            const range = 6;
            const start = Math.max(0, index - range);
            const end = Math.min(lazyLoadImages.length - 1, index + range);

            for (let i = start; i <= end; i++) {
                const img = lazyLoadImages[i];
                if (!img.src) {
                    img.src = img.dataset.src;
                    img.onload = () => { img.classList.add('loaded'); };
                }
            }
        }

        lazyLoadImages.forEach(img => { observer.observe(img); });

        // Liczba wyświetlonych produktów
        document.getElementById('displayedProductCount').textContent = data.length;
    }



    function getDeliveryClass(days) {
        if (days <= 1) return 'Availability1Day';
        if (days <= 3) return 'Availability3Days';
        if (days <= 7) return 'Availability7Days';
        return 'Availability14Days';
    }

    function hexToRgba(hex, alpha) {
        let r = 0, g = 0, b = 0;
        if (hex.length == 4) {
            r = parseInt(hex[1] + hex[1], 16);
            g = parseInt(hex[2] + hex[2], 16);
            b = parseInt(hex[3] + hex[3], 16);
        } else if (hex.length == 7) {
            r = parseInt(hex[1] + hex[2], 16);
            g = parseInt(hex[3] + hex[4], 16);
            b = parseInt(hex[5] + hex[6], 16);
        }
        return `rgba(${r}, ${g}, ${b}, ${alpha})`;
    }
    function renderChart(data) {
        const colorCounts = {
            prOnlyMe: 0,
            prToHigh: 0,
            prMid: 0,
            prGood: 0,
            prIdeal: 0,
            prToLow: 0
        };

        data.forEach(item => {
            if (!item.isRejected) {
                colorCounts[item.colorClass] = (colorCounts[item.colorClass] || 0) + 1;
            }
        });

        const chartData = [
            colorCounts.prOnlyMe,
            colorCounts.prToHigh,
            colorCounts.prMid,
            colorCounts.prGood,
            colorCounts.prIdeal,
            colorCounts.prToLow
        ];

        if (chartInstance) {
            chartInstance.data.datasets[0].data = chartData;
            chartInstance.update();
        } else {
            const ctx = document.getElementById('colorChart').getContext('2d');
            chartInstance = new Chart(ctx, {
                type: 'doughnut',
                data: {
                    labels: ['Solo', 'Zawyżona', 'Suboptymalna', 'Konkurencyjna', 'Strategiczna', 'Zaniżona'],
                    datasets: [{
                        data: chartData,
                        backgroundColor: [
                            'rgba(180, 180, 180, 0.8)', // kolor dla prOnlyMe
                            'rgba(171, 37, 32, 0.8)',
                            'rgba(224, 168, 66, 0.8)',
                            'rgba(117, 152, 112, 0.8)',
                            'rgba(0, 145, 123, 0.8)',
                            'rgba(6, 6, 6, 0.8)'
                        ],
                        borderColor: [
                            'rgba(180, 180, 180, 1)',
                            'rgba(171, 37, 32, 1)',
                            'rgba(224, 168, 66, 1)',
                            'rgba(117, 152, 112, 1)',
                            'rgba(0, 145, 123, 1)',
                            'rgba(6, 6, 6, 1)'
                        ],
                        borderWidth: 1
                    }]
                },
                options: {
                    aspectRatio: 1,
                    cutout: '60%',
                    plugins: {
                        legend: {
                            display: false
                        },
                        tooltip: {
                            callbacks: {
                                label: function (context) {
                                    var value = context.parsed;
                                    return 'Produkty: ' + value;
                                }
                            }
                        }
                    }
                }
            });
        }
    }

    const debouncedRenderChart = debounce(renderChart, 600);



    document.getElementById('openMarginSettingsBtn').addEventListener('click', function () {
        // Załóżmy, że globalny obiekt marginSettings zawiera pobrane ustawienia:
        // { useMarginForSimulation: true, enforceMinimalMargin: true, minimalMarginPercent: 0.00 }
        document.getElementById('useMarginForSimulationInput').value = marginSettings.useMarginForSimulation.toString();
        document.getElementById('enforceMinimalMarginInput').value = marginSettings.enforceMinimalMargin.toString();
        document.getElementById('minimalMarginPercentInput').value = marginSettings.minimalMarginPercent;

        // Otwórz modal przy użyciu Bootstrapa/jQuery
        $('#marginSettingsModal').modal('show');
    });
    document.getElementById('saveMarginSettingsBtn').addEventListener('click', function () {
        const updatedMarginSettings = {
            StoreId: storeId,  // zakładamy, że storeId jest globalnie zdefiniowane
            UseMarginForSimulation: document.getElementById('useMarginForSimulationInput').value === 'true',
            EnforceMinimalMargin: document.getElementById('enforceMinimalMarginInput').value === 'true',
            MinimalMarginPercent: parseFloat(document.getElementById('minimalMarginPercentInput').value)
        };

        fetch('/PriceHistory/SaveMarginSettings', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(updatedMarginSettings)
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    // Zaktualizuj globalny obiekt marginSettings
                    marginSettings = updatedMarginSettings;
                    $('#marginSettingsModal').modal('hide');
                    // Odśwież widok cen
                    loadPrices();
                } else {
                    alert('Błąd zapisu ustawień: ' + data.message);
                }
            })
            .catch(error => console.error('Błąd zapisu ustawień marży:', error));
    });



    function updateColorCounts(data) {
        const colorCounts = {
            prOnlyMe: 0,
            prToHigh: 0,
            prMid: 0,
            prGood: 0,
            prIdeal: 0,
            prToLow: 0
        };

        data.forEach(item => {
            colorCounts[item.colorClass] = (colorCounts[item.colorClass] || 0) + 1;
        });

        document.querySelector('label[for="prOnlyMeCheckbox"]').textContent = `Solo (${colorCounts.prOnlyMe})`;
        document.querySelector('label[for="prToHighCheckbox"]').textContent = `Zawyżona (${colorCounts.prToHigh})`;
        document.querySelector('label[for="prMidCheckbox"]').textContent = `Suboptymalna (${colorCounts.prMid})`;
        document.querySelector('label[for="prGoodCheckbox"]').textContent = `Konkurencyjna (${colorCounts.prGood})`;
        document.querySelector('label[for="prIdealCheckbox"]').textContent = `Strategiczna (${colorCounts.prIdeal})`;
        document.querySelector('label[for="prToLowCheckbox"]').textContent = `Zaniżona (${colorCounts.prToLow})`;
    }

    function filterPricesAndUpdateUI(resetPageFlag = true) {
        if (resetPageFlag) {
            resetPage();
        }
        showLoading();

        setTimeout(() => {
            // 1. Zaczynamy od kopii wszystkich cen
            let filteredPrices = [...allPrices];

            // 2. Pobieramy wartości z pól wyszukiwania (surowe)
            const productSearchRaw = document.getElementById('productSearch').value.trim();
            const storeSearchRaw = document.getElementById('storeSearch').value.trim();

            // 3. Jeśli wpisano tekst w wyszukiwarce produktu, filtrujemy ceny łącząc productName i ean
            if (productSearchRaw) {
                const sanitizedProductSearch = productSearchRaw
                    .replace(/[^a-zA-Z0-9\s.-]/g, '')
                    .toLowerCase()
                    .replace(/\s+/g, '');
                filteredPrices = filteredPrices.filter(price => {
                    const combined = (price.productName || '') + ' ' + (price.ean || '');
                    const combinedSanitized = combined
                        .toLowerCase()
                        .replace(/[^a-zA-Z0-9\s.-]/g, '')
                        .replace(/\s+/g, '');
                    return combinedSanitized.includes(sanitizedProductSearch);
                });
            }

            // 4. Jeśli wpisano tekst w wyszukiwarce sklepu, filtrujemy wg storeName
            if (storeSearchRaw) {
                const sanitizedStoreSearch = storeSearchRaw
                    .replace(/[^a-zA-Z0-9\s.-]/g, '')
                    .toLowerCase()
                    .replace(/\s+/g, '');
                filteredPrices = filteredPrices.filter(price => {
                    const storeNameSanitized = (price.storeName || '')
                        .toLowerCase()
                        .replace(/[^a-zA-Z0-9\s.-]/g, '')
                        .replace(/\s+/g, '');
                    return storeNameSanitized.includes(sanitizedStoreSearch);
                });
            }

            // 5. Sortowanie trafień wyszukiwania dla produktu (łączone productName + ean)
            const sanitizedProductSearchTerm = productSearchRaw
                ? productSearchRaw.replace(/[^a-zA-Z0-9\s.-]/g, '').toLowerCase().replace(/\s+/g, '')
                : '';
            if (sanitizedProductSearchTerm !== '') {
                filteredPrices.sort((a, b) => {
                    const aCombined = ((a.productName || '') + ' ' + (a.ean || ''))
                        .toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
                    const bCombined = ((b.productName || '') + ' ' + (b.ean || ''))
                        .toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
                    const exactMatchIndexA = getExactMatchIndex(aCombined, sanitizedProductSearchTerm);
                    const exactMatchIndexB = getExactMatchIndex(bCombined, sanitizedProductSearchTerm);
                    if (exactMatchIndexA !== exactMatchIndexB) {
                        return exactMatchIndexA - exactMatchIndexB;
                    }
                    const matchLengthA = getLongestMatchLength(aCombined, sanitizedProductSearchTerm);
                    const matchLengthB = getLongestMatchLength(bCombined, sanitizedProductSearchTerm);
                    if (matchLengthA !== matchLengthB) {
                        return matchLengthB - matchLengthA;
                    }
                    return a.productName.localeCompare(b.productName);
                });
            }

            // 6. Pozostałe filtry (kategorie, kolory, flagi)
            filteredPrices = filterPricesByCategoryAndColorAndFlag(filteredPrices);

            // 7. Filtrowanie produktów odrzuconych
            if (sortingState.showRejected) {
                filteredPrices = filteredPrices.filter(item => item.isRejected);
            } else {
                filteredPrices = filteredPrices.filter(item => !item.isRejected);
            }

            // 8. Sortowanie wg ustawionych kryteriów
            if (sortingState.sortName !== null) {
                if (sortingState.sortName === 'asc') {
                    filteredPrices.sort((a, b) => a.productName.localeCompare(b.productName));
                } else {
                    filteredPrices.sort((a, b) => b.productName.localeCompare(a.productName));
                }
            } else if (sortingState.sortPrice !== null) {
                if (sortingState.sortPrice === 'asc') {
                    filteredPrices.sort((a, b) => a.lowestPrice - b.lowestPrice);
                } else {
                    filteredPrices.sort((a, b) => b.lowestPrice - a.lowestPrice);
                }
            } else if (sortingState.sortRaiseAmount !== null) {
                filteredPrices = filteredPrices.filter(item => !item.isRejected && item.savings !== null);
                if (sortingState.sortRaiseAmount === 'asc') {
                    filteredPrices.sort((a, b) => a.savings - b.savings);
                } else {
                    filteredPrices.sort((a, b) => b.savings - a.savings);
                }
            } else if (sortingState.sortRaisePercentage !== null) {
                filteredPrices = filteredPrices.filter(item => !item.isRejected && item.savings !== null);
                if (sortingState.sortRaisePercentage === 'asc') {
                    filteredPrices.sort((a, b) => a.percentageDifference - b.percentageDifference);
                } else {
                    filteredPrices.sort((a, b) => b.percentageDifference - a.percentageDifference);
                }
            } else if (sortingState.sortLowerAmount !== null) {
                filteredPrices = filteredPrices.filter(item => !item.isRejected && item.savings === null && item.priceDifference !== 0);
                if (sortingState.sortLowerAmount === 'asc') {
                    filteredPrices.sort((a, b) => a.priceDifference - b.priceDifference);
                } else {
                    filteredPrices.sort((a, b) => b.priceDifference - a.priceDifference);
                }
            } else if (sortingState.sortLowerPercentage !== null) {
                filteredPrices = filteredPrices.filter(item => !item.isRejected && item.savings === null && item.priceDifference !== 0);
                if (sortingState.sortLowerPercentage === 'asc') {
                    filteredPrices.sort((a, b) => a.percentageDifference - b.percentageDifference);
                } else {
                    filteredPrices.sort((a, b) => b.percentageDifference - a.percentageDifference);
                }
            } else if (sortingState.sortMarginAmount !== null) {
                filteredPrices = filteredPrices.filter(item => item.marginAmount !== null);
                if (sortingState.sortMarginAmount === 'asc') {
                    filteredPrices.sort((a, b) => a.marginAmount - b.marginAmount);
                } else {
                    filteredPrices.sort((a, b) => b.marginAmount - a.marginAmount);
                }
            } else if (sortingState.sortMarginPercentage !== null) {
                filteredPrices = filteredPrices.filter(item => item.marginPercentage !== null);
                if (sortingState.sortMarginPercentage === 'asc') {
                    filteredPrices.sort((a, b) => a.marginPercentage - b.marginPercentage);
                } else {
                    filteredPrices.sort((a, b) => b.marginPercentage - a.marginPercentage);
                }
            }

            // 9. Aktualizacja widoku
            renderPrices(filteredPrices);
            debouncedRenderChart(filteredPrices);
            updateColorCounts(filteredPrices);
            updateFlagCounts(filteredPrices);

            hideLoading();
        }, 0);
    }

    // Nowa funkcja highlightMatches z opcjonalnym trzecim argumentem określającym klasę CSS
    function highlightMatches(fullText, searchTerm, customClass) {
        if (!searchTerm) return fullText;
        const escapedTerm = searchTerm.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        const regex = new RegExp(escapedTerm, 'gi');
        const cssClass = customClass || "highlighted-text";
        return fullText.replace(regex, (match) => `<span class="${cssClass}">${match}</span>`);
    }

    document.getElementById('showRejectedButton').addEventListener('click', function () {
        sortingState.showRejected = !sortingState.showRejected;
        if (sortingState.showRejected) {
            this.classList.add('active');
        } else {
            this.classList.remove('active');
        }
        resetSortingStates('showRejected');
        filterPricesAndUpdateUI();
    });

    function resetSortingStates(except) {
        for (let key in sortingState) {
            if (key !== except) {
                if (key === 'showRejected') {
                    sortingState[key] = false;
                    let button = document.getElementById('showRejectedButton');
                    if (button) {
                        button.classList.remove('active');
                    }
                } else {
                    sortingState[key] = null;
                    let button = document.getElementById(key);
                    if (button) {
                        button.innerHTML = getDefaultButtonLabel(key);
                        button.classList.remove('active');
                    }
                }
            }
        }
    }

    function updateMarginSortButtonsVisibility() {
        const hasMarginData = allPrices.some(item => item.marginAmount !== null && item.marginPercentage !== null);
        const sortMarginAmountButton = document.getElementById('sortMarginAmount');
        const sortMarginPercentageButton = document.getElementById('sortMarginPercentage');
        if (hasMarginData) {
            sortMarginAmountButton.style.display = '';
            sortMarginPercentageButton.style.display = '';
        } else {
            sortMarginAmountButton.style.display = 'none';
            sortMarginPercentageButton.style.display = 'none';
        }
    }

    function getDefaultButtonLabel(key) {
        switch (key) {
            case 'sortName':
                return 'A-Z';
            case 'sortPrice':
                return 'Cena';
            case 'sortRaiseAmount':
                return 'Podnieś PLN';
            case 'sortRaisePercentage':
                return 'Podnieś %';
            case 'sortLowerAmount':
                return 'Obniż PLN';
            case 'sortLowerPercentage':
                return 'Obniż %';
            case 'sortMarginAmount':
                return 'Marża PLN';
            case 'sortMarginPercentage':
                return 'Marża %';
            case 'showRejected':
                return 'Pokaż Odrzucone';
            default:
                return '';
        }
    }

    function getExactMatchIndex(text, searchTerm) {
        return text.indexOf(searchTerm);
    }

    function getLongestMatchLength(text, searchTerm) {
        let maxLength = 0;

        for (let i = 0; i < text.length; i++) {
            for (let j = i; j <= text.length; j++) {
                const substring = text.slice(i, j);
                if (searchTerm.includes(substring) && substring.length > maxLength) {
                    maxLength = substring.length;
                }
            }
        }

        return maxLength;
    }

    const debouncedFilterPrices = debounce(function () {
        filterPricesAndUpdateUI();
    }, 300);

    document.getElementById('productSearch').addEventListener('input', debouncedFilterPrices);

    document.querySelectorAll('.colorFilter, .flagFilter, .positionFilter, .deliveryFilterMyStore, .deliveryFilterCompetitor, .externalPriceFilter')
        .forEach(function (checkbox) {
            checkbox.addEventListener('change', function () {
                showLoading(); // <-- pokaż loader
                filterPricesAndUpdateUI();
            });
        });

    document.getElementById('bidFilter').addEventListener('change', function () {
        filterPricesAndUpdateUI();
    });

    document.getElementById('suspiciouslyLowFilter').addEventListener('change', function () {
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortName').addEventListener('click', function () {
        if (sortingState.sortName === null) {
            sortingState.sortName = 'asc';
            this.innerHTML = 'A-Z ↑';
            this.classList.add('active');
        } else if (sortingState.sortName === 'asc') {
            sortingState.sortName = 'desc';
            this.innerHTML = 'A-Z ↓';
            this.classList.add('active');
        } else {
            sortingState.sortName = null;
            this.innerHTML = 'A-Z';
            this.classList.remove('active');
        }
        resetSortingStates('sortName');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortPrice').addEventListener('click', function () {
        if (sortingState.sortPrice === null) {
            sortingState.sortPrice = 'asc';
            this.innerHTML = 'Cena ↑';
            this.classList.add('active');
        } else if (sortingState.sortPrice === 'asc') {
            sortingState.sortPrice = 'desc';
            this.innerHTML = 'Cena ↓';
            this.classList.add('active');
        } else {
            sortingState.sortPrice = null;
            this.innerHTML = 'Cena';
            this.classList.remove('active');
        }
        resetSortingStates('sortPrice');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortRaiseAmount').addEventListener('click', function () {
        if (sortingState.sortRaiseAmount === null) {
            sortingState.sortRaiseAmount = 'asc';
            this.innerHTML = 'Podnieś PLN ↑';
            this.classList.add('active');
        } else if (sortingState.sortRaiseAmount === 'asc') {
            sortingState.sortRaiseAmount = 'desc';
            this.innerHTML = 'Podnieś PLN ↓';
            this.classList.add('active');
        } else {
            sortingState.sortRaiseAmount = null;
            this.innerHTML = 'Podnieś PLN';
            this.classList.remove('active');
        }
        resetSortingStates('sortRaiseAmount');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortRaisePercentage').addEventListener('click', function () {
        if (sortingState.sortRaisePercentage === null) {
            sortingState.sortRaisePercentage = 'asc';
            this.innerHTML = 'Podnieś % ↑';
            this.classList.add('active');
        } else if (sortingState.sortRaisePercentage === 'asc') {
            sortingState.sortRaisePercentage = 'desc';
            this.innerHTML = 'Podnieś % ↓';
            this.classList.add('active');
        } else {
            sortingState.sortRaisePercentage = null;
            this.innerHTML = 'Podnieś %';
            this.classList.remove('active');
        }
        resetSortingStates('sortRaisePercentage');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortLowerAmount').addEventListener('click', function () {
        if (sortingState.sortLowerAmount === null) {
            sortingState.sortLowerAmount = 'asc';
            this.innerHTML = 'Obniż PLN ↑';
            this.classList.add('active');
        } else if (sortingState.sortLowerAmount === 'asc') {
            sortingState.sortLowerAmount = 'desc';
            this.innerHTML = 'Obniż PLN ↓';
            this.classList.add('active');
        } else {
            sortingState.sortLowerAmount = null;
            this.innerHTML = 'Obniż PLN';
            this.classList.remove('active');
        }
        resetSortingStates('sortLowerAmount');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortLowerPercentage').addEventListener('click', function () {
        if (sortingState.sortLowerPercentage === null) {
            sortingState.sortLowerPercentage = 'asc';
            this.innerHTML = 'Obniż % ↑';
            this.classList.add('active');
        } else if (sortingState.sortLowerPercentage === 'asc') {
            sortingState.sortLowerPercentage = 'desc';
            this.innerHTML = 'Obniż % ↓';
            this.classList.add('active');
        } else {
            sortingState.sortLowerPercentage = null;
            this.innerHTML = 'Obniż %';
            this.classList.remove('active');
        }
        resetSortingStates('sortLowerPercentage');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortMarginAmount').addEventListener('click', function () {
        if (sortingState.sortMarginAmount === null) {
            sortingState.sortMarginAmount = 'asc';
            this.innerHTML = 'Marża PLN ↑';
            this.classList.add('active');
        } else if (sortingState.sortMarginAmount === 'asc') {
            sortingState.sortMarginAmount = 'desc';
            this.innerHTML = 'Marża PLN ↓';
            this.classList.add('active');
        } else {
            sortingState.sortMarginAmount = null;
            this.innerHTML = 'Marża PLN';
            this.classList.remove('active');
        }
        resetSortingStates('sortMarginAmount');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortMarginPercentage').addEventListener('click', function () {
        if (sortingState.sortMarginPercentage === null) {
            sortingState.sortMarginPercentage = 'asc';
            this.innerHTML = 'Marża % ↑';
            this.classList.add('active');
        } else if (sortingState.sortMarginPercentage === 'asc') {
            sortingState.sortMarginPercentage = 'desc';
            this.innerHTML = 'Marża % ↓';
            this.classList.add('active');
        } else {
            sortingState.sortMarginPercentage = null;
            this.innerHTML = 'Marża %';
            this.classList.remove('active');
        }
        resetSortingStates('sortMarginPercentage');
        filterPricesAndUpdateUI();
    });

    document.getElementById('usePriceDifference').addEventListener('change', function () {
        usePriceDifference = this.checked;
        updateUnits(usePriceDifference);
        /* updatePricesDebounced();*/
    });

    document.getElementById('storeSearch').addEventListener('input', debouncedFilterPrices);

    document.getElementById('price1').addEventListener('input', function () {
        setPrice1 = parseFloat(this.value);
        /*  updatePricesDebounced();*/
    });

    document.getElementById('price2').addEventListener('input', function () {
        setPrice2 = parseFloat(this.value);
        /*  updatePricesDebounced();*/
    });
    document.getElementById('stepPrice').addEventListener('input', function () {
        setStepPrice = parseFloat(this.value);

        /*   updatePricesDebounced();*/
    });

    document.getElementById('productSearch').addEventListener('input', function () {
        currentPage = 1;
        window.scrollTo(0, 0);
        debouncedFilterPrices();
    });

    document.getElementById('sourceSelect').addEventListener('change', function () {
        loadStores();
        loadPrices();
    });

    const exportButton = document.getElementById("exportToExcelButton");
    if (exportButton) {
        exportButton.addEventListener("click", exportToExcelXLSX);
    }

    document.getElementById('savePriceValues').addEventListener('click', function () {
        const price1 = parseFloat(document.getElementById('price1').value);
        const price2 = parseFloat(document.getElementById('price2').value);
        const stepPrice = parseFloat(document.getElementById('stepPrice').value);
        const usePriceDiff = document.getElementById('usePriceDifference').checked;
        const data = {
            StoreId: storeId,
            SetPrice1: price1,
            SetPrice2: price2,
            PriceStep: stepPrice,
            UsePriceDiff: usePriceDiff
        };

        fetch('/PriceHistory/SavePriceValues', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(data)
        })
            .then(response => response.json())
            .then(response => {
                if (response.success) {
                    setPrice1 = price1;
                    setPrice2 = price2;
                    setStepPrice = stepPrice;
                    usePriceDifferenceGlobal = usePriceDiff;
                    loadPrices();
                } else {
                    alert('Błąd zapisu wartości: ' + response.message);
                }
            })
            .catch(error => console.error('Błąd zapisu wartości:', error));
    });

    const modal = document.getElementById('flagModal');
    const span = document.getElementsByClassName('close')[0];

    span.onclick = function () {
        modal.style.display = 'none';
    };

    window.onclick = function (event) {
        if (event.target == modal) {
            modal.style.display = 'none';
        }
    };

    document.getElementById('saveFlagsButton').addEventListener('click', function () {
        const selectedFlags = Array.from(document.querySelectorAll('.flagCheckbox:checked')).map(checkbox => parseInt(checkbox.value));
        const data = {
            productId: selectedProductId,
            flagIds: selectedFlags
        };

        fetch('/ProductFlags/AssignFlagsToProduct', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(data)
        })
            .then(response => response.json())
            .then(response => {
                if (response.success) {
                    modal.style.display = 'none';

                    fetch(`/ProductFlags/GetFlagsForProduct?productId=${selectedProductId}`)
                        .then(res => res.json())
                        .then(updatedFlagIds => {
                            const numericProductId = parseInt(selectedProductId);
                            const priceItem = allPrices.find(item => item.productId === numericProductId);
                            if (priceItem) {
                                priceItem.flagIds = updatedFlagIds;
                            } else {
                                console.error('Product not found in allPrices:', selectedProductId);
                            }

                            updateProductFlagsInUI(selectedProductId);

                            updateFlagCounts(allPrices);
                        })
                        .catch(error => console.error('Error fetching updated flags for product:', error));
                } else {
                    alert('Error assigning flags: ' + response.message);
                }
            })
            .catch(error => console.error('Error assigning flags:', error));
    });

    function updateProductFlagsInUI(productId) {
        const numericProductId = parseInt(productId);

        const productElement = document.querySelector(`.price-box[data-product-id='${productId}']`);

        if (productElement) {
            const existingFlagsContainer = productElement.querySelector('.flags-container');

            const priceItem = allPrices.find(item => item.productId === numericProductId);

            if (priceItem) {
                const newFlagsContainer = createFlagsContainer(priceItem);

                if (existingFlagsContainer) {
                    existingFlagsContainer.parentNode.replaceChild(newFlagsContainer, existingFlagsContainer);
                } else {
                    productElement.appendChild(newFlagsContainer);
                }
            } else {
                console.error('Product not found in allPrices:', productId);
            }
        }
    }

    function createFlagsContainer(item) {
        const flagsContainer = document.createElement('div');
        flagsContainer.className = 'flags-container';
        if (item.flagIds && item.flagIds.length > 0) {
            item.flagIds.forEach(function (flagId) {
                const flag = flags.find(function (f) { return f.FlagId === flagId; });
                if (flag) {
                    const flagSpan = document.createElement('span');
                    flagSpan.className = 'flag';
                    flagSpan.style.color = flag.FlagColor;
                    flagSpan.style.border = '2px solid ' + flag.FlagColor;
                    flagSpan.style.backgroundColor = hexToRgba(flag.FlagColor, 0.3);
                    flagSpan.innerHTML = flag.FlagName;
                    flagsContainer.appendChild(flagSpan);
                }
            });
        }
        return flagsContainer;
    }

    loadStores();
    loadPrices();

    function showLoading() {
        document.getElementById("loadingOverlay").style.display = "flex";
    }

    function hideLoading() {
        document.getElementById("loadingOverlay").style.display = "none";
    }
});



