document.addEventListener("DOMContentLoaded", function () {

    let allPrices = [];
    let currentScrapId = null;
    let latestScrapDate = null;
    let currentlyFilteredPrices = [];
    let chartInstance = null;
    let currentPage = 1;
    const itemsPerPage = 1000;
    let myStoreName = "";

    let thresholdsState = {
        pct: { greenDark: 20, green: 10, greenLight: 1, redLight: 1, red: 10, redDark: 20 },
        amt: { greenDark: 50, green: 20, greenLight: 5, redLight: 5, red: 20, redDark: 50 }
    };
    let currentMode = 'pct';

    let producerSettings = {
        comparisonSource: 1,
        identifierForSimulation: 'EAN'
    };

    let selectedFlagsInclude = new Set();
    let selectedFlagsExclude = new Set();
    let selectedAdvancedIncludes = new Set();
    let selectedAdvancedExcludes = new Set();

    let sortingState = {
        sortName: null,
        sortPrice: null,
        sortDeltaPercent: null,
        sortDeltaAmount: null,
        sortViolationDuration: null,
        sortStoresViolating: null,
        sortCeneoSales: null,
        sortSalesTrendAmount: null,
        sortSalesTrendPercent: null
    };

    let positionSlider, offerSlider, myPriceSlider;

    const BUCKETS_ORDERED = [
        { key: 'producer-no-competition', label: 'Brak konkurencji', color: 'rgba(136, 136, 136, 0.85)' },
        { key: 'producer-no-reference', label: 'Brak ceny ref.', color: 'rgba(187, 187, 187, 0.85)' },
        { key: 'producer-deep-above', label: 'Mocno powyżej', color: 'rgba(0, 100, 0, 0.85)' },
        { key: 'producer-above', label: 'Powyżej', color: 'rgba(34, 139, 34, 0.85)' },
        { key: 'producer-minor-above', label: 'Lekko powyżej', color: 'rgba(144, 238, 144, 0.85)' },
        { key: 'producer-equal', label: 'Cena zgodna', color: 'rgba(65, 105, 225, 0.85)' },
        { key: 'producer-minor-below', label: 'Lekko poniżej', color: 'rgba(255, 99, 71, 0.85)' },
        { key: 'producer-violation', label: 'Naruszenie', color: 'rgba(220, 20, 60, 0.85)' },
        { key: 'producer-deep-violation', label: 'Drastyczne naruszenie', color: 'rgba(139, 0, 0, 0.85)' }
    ];
    const BUCKET_LABELS = Object.fromEntries(BUCKETS_ORDERED.map(b => [b.key, b.label]));

    const BUCKET_BADGE_LABELS = {
        'producer-deep-violation': 'Drastyczne naruszenie',
        'producer-violation': 'Naruszenie',
        'producer-minor-below': 'Cena lekko poniżej',
        'producer-equal': 'Cena zgodna',
        'producer-minor-above': 'Cena lekko powyżej',
        'producer-above': 'Cena powyżej',
        'producer-deep-above': 'Cena mocno powyżej'
    };

    const BUCKET_TO_CHECKBOX = {
        'producer-no-competition': 'bucketNoCompetition',
        'producer-no-reference': 'bucketNoReference',
        'producer-deep-above': 'bucketDeepAbove',
        'producer-above': 'bucketAbove',
        'producer-minor-above': 'bucketMinorAbove',
        'producer-equal': 'bucketEqual',
        'producer-minor-below': 'bucketMinorBelow',
        'producer-violation': 'bucketViolation',
        'producer-deep-violation': 'bucketDeepViolation'
    };

    const massActions = new window.MassActions({
        storeId: storeId,
        isAllegro: false,
        storageKey: `selectedProducts_${storeId}`,
        flags: typeof flags !== 'undefined' ? flags : [],
        getAllPrices: () => allPrices,
        getFilteredPrices: () => currentlyFilteredPrices,
        getProductIdentifier: (product) => {
            switch (producerSettings.identifierForSimulation) {
                case 'ID': return { label: 'ID', value: product.externalId ? product.externalId.toString() : null };
                case 'ProducerCode': return { label: 'SKU', value: product.producerCode || null };
                default: return { label: 'EAN', value: product.ean || null };
            }
        },
        showLoading: showLoading, hideLoading: hideLoading,
        showGlobalUpdate: showGlobalUpdate, showGlobalNotification: showGlobalNotification,
        onFlagsUpdated: () => loadPrices(),
        onAutomationsUpdated: () => loadPrices()
    });
    massActions.init();

    function formatPricePL(value, includeUnit = true) {
        if (value === null || value === undefined || isNaN(parseFloat(value))) return "N/A";
        const numberValue = parseFloat(value);
        const formatted = numberValue.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        return includeUnit ? formatted + ' PLN' : formatted;
    }

    function pluralizePl(n, singular, fewForm, manyForm) {
        if (n === 1) return singular;
        const lastTwo = Math.abs(n) % 100;
        const lastOne = Math.abs(n) % 10;
        if (lastTwo >= 12 && lastTwo <= 14) return manyForm;
        if (lastOne >= 2 && lastOne <= 4) return fewForm;
        return manyForm;
    }

    function formatViolationDuration(hours, reachedMaxWindow) {
        if (reachedMaxWindow) return '+7 dni naruszenia';
        if (hours == null || isNaN(hours)) return 'Brak danych';
        if (hours >= 168) return '+7 dni naruszenia';
        if (hours < 24) {
            const h = Math.max(1, Math.round(hours));
            return `${h}h naruszenia`;
        }
        const days = Math.floor(hours / 24);
        const dayWord = pluralizePl(days, 'dzień', 'dni', 'dni');
        return `${days} ${dayWord} naruszenia`;
    }

    function formatHoursAgo(hours) {
        if (hours == null || isNaN(hours)) return 'N/A';
        if (hours < 1) return '< 1h';
        if (hours < 24) {
            const h = Math.round(hours);
            return `${h}h`;
        }
        const days = Math.floor(hours / 24);
        const dayWord = pluralizePl(days, 'dzień', 'dni', 'dni');
        return `${days} ${dayWord}`;
    }

    function formatDurationShort(hours) {
        if (hours == null || isNaN(hours)) return 'N/A';
        if (hours >= 168) return '7+ dni';
        if (hours < 1) return '< 1h';
        if (hours < 24) {
            const h = Math.max(1, Math.round(hours));
            return `${h}h`;
        }
        const days = Math.floor(hours / 24);
        const dayWord = pluralizePl(days, 'dzień', 'dni', 'dni');
        return `${days} ${dayWord}`;
    }

    function debounce(func, wait) {
        let timeout;
        return function (...args) {
            const ctx = this;
            clearTimeout(timeout);
            timeout = setTimeout(() => func.apply(ctx, args), wait);
        };
    }

    function showLoading() { document.getElementById("loadingOverlay").style.display = "flex"; }
    function hideLoading() { document.getElementById("loadingOverlay").style.display = "none"; }

    let _notifTimeout, _updateTimeout;
    function showGlobalNotification(message) {
        const notif = document.getElementById("globalNotification");
        const upd = document.getElementById("globalUpdate");
        if (!notif) return;
        if (upd && upd.style.display === "block") { upd.style.display = "none"; if (_updateTimeout) clearTimeout(_updateTimeout); }
        if (_notifTimeout) clearTimeout(_notifTimeout);
        notif.innerHTML = message;
        notif.style.display = "block";
        _notifTimeout = setTimeout(() => notif.style.display = "none", 4000);
    }
    function showGlobalUpdate(message) {
        const upd = document.getElementById("globalUpdate");
        const notif = document.getElementById("globalNotification");
        if (!upd) return;
        if (notif && notif.style.display === "block") { notif.style.display = "none"; if (_notifTimeout) clearTimeout(_notifTimeout); }
        if (_updateTimeout) clearTimeout(_updateTimeout);
        upd.innerHTML = message;
        upd.style.display = "block";
        _updateTimeout = setTimeout(() => upd.style.display = "none", 4000);
    }

    function highlightMatches(fullText, searchTerm, customClass) {
        if (!searchTerm || !fullText) return fullText;
        const escapedTerm = searchTerm.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        const regex = new RegExp(escapedTerm, 'gi');
        const cssClass = customClass || "highlighted-text";
        return fullText.replace(regex, (match) => `<span class="${cssClass}">${match}</span>`);
    }

    function hexToRgba(hex, alpha) {
        let r = 0, g = 0, b = 0;
        if (hex.length == 4) { r = parseInt(hex[1] + hex[1], 16); g = parseInt(hex[2] + hex[2], 16); b = parseInt(hex[3] + hex[3], 16); }
        else if (hex.length == 7) { r = parseInt(hex[1] + hex[2], 16); g = parseInt(hex[3] + hex[4], 16); b = parseInt(hex[5] + hex[6], 16); }
        return `rgba(${r}, ${g}, ${b}, ${alpha})`;
    }

    function getStockBadge(inStock) {
        if (inStock === true) return '<span class="stock-available">Dostępny</span>';
        if (inStock === false) return '<span class="stock-unavailable">Niedostępny</span>';
        return '<span class="BD">Brak danych</span>';
    }

    function getOfferText(count) {
        if (count === 1) return `${count} Oferta`;
        const lastDigit = count % 10;
        if (count > 10 && count < 20) return `${count} Ofert`;
        if ([2, 3, 4].includes(lastDigit)) return `${count} Oferty`;
        return `${count} Ofert`;
    }

    positionSlider = document.getElementById('positionRangeSlider');
    const positionRangeInput = document.getElementById('positionRange');
    noUiSlider.create(positionSlider, {
        start: [1, 200], connect: true, range: { 'min': 1, 'max': 200 }, step: 1,
        format: wNumb({ decimals: 0 })
    });
    positionSlider.noUiSlider.on('update', function (values) {
        positionRangeInput.textContent = values.map(v => parseInt(v) === 60 ? 'Schowany' : 'Pozycja ' + v).join(' - ');
    });
    positionSlider.noUiSlider.on('change', () => filterPricesAndUpdateUI());

    offerSlider = document.getElementById('offerRangeSlider');
    const offerRangeInput = document.getElementById('offerRange');
    noUiSlider.create(offerSlider, {
        start: [1, 1], connect: true, range: { 'min': 1, 'max': 1 }, step: 1,
        format: wNumb({ decimals: 0 })
    });
    offerSlider.noUiSlider.on('update', function (values) {
        offerRangeInput.textContent = values.map(v => {
            const i = parseInt(v);
            let s = ' Ofert';
            if (i === 1) s = ' Oferta';
            else if (i >= 2 && i <= 4) s = ' Oferty';
            return i + s;
        }).join(' - ');
    });
    offerSlider.noUiSlider.on('change', () => filterPricesAndUpdateUI());

    myPriceSlider = document.getElementById('myPriceRangeSlider');
    const myPriceRangeInput = document.getElementById('myPriceRange');
    noUiSlider.create(myPriceSlider, {
        start: [0, 10000], connect: true, range: { 'min': 0, 'max': 10000 },
        format: wNumb({ decimals: 2, thousand: ' ', suffix: ' PLN' })
    });
    myPriceSlider.noUiSlider.on('update', (values) => myPriceRangeInput.textContent = values.join(' - '));
    myPriceSlider.noUiSlider.on('change', () => filterPricesAndUpdateUI());

    function loadPrices() {
        showLoading();

        fetch(`/PriceHistory/GetPricesForProducer?storeId=${storeId}`)
            .then(r => r.json())
            .then(response => {
                if (response.error) {
                    showGlobalNotification(`<p>${response.error}</p>`);
                    return;
                }

                myStoreName = response.myStoreName;
                currentScrapId = response.latestScrapId;
                latestScrapDate = response.latestScrapDate || null;

                const ps = response.producerSettings;
                const t = ps.thresholds || {};
                producerSettings.comparisonSource = ps.comparisonSource;
                producerSettings.identifierForSimulation = ps.identifierForSimulation || 'EAN';

                const parseOr = (val, fallback) => {
                    const n = parseFloat(val);
                    return isNaN(n) ? fallback : n;
                };

                thresholdsState.pct = {
                    greenDark: parseOr(t.greenDarkPct, 20),
                    green: parseOr(t.greenPct, 10),
                    greenLight: parseOr(t.greenLightPct, 1),
                    redLight: parseOr(t.redLightPct, 1),
                    red: parseOr(t.redPct, 10),
                    redDark: parseOr(t.redDarkPct, 20)
                };
                thresholdsState.amt = {
                    greenDark: parseOr(t.greenDarkAmt, 50),
                    green: parseOr(t.greenAmt, 20),
                    greenLight: parseOr(t.greenLightAmt, 5),
                    redLight: parseOr(t.redLightAmt, 5),
                    red: parseOr(t.redAmt, 20),
                    redDark: parseOr(t.redDarkAmt, 50)
                };
                currentMode = ps.useAmount ? 'amt' : 'pct';

                document.getElementById('useAmountToggle').checked = (currentMode === 'amt');
                populateInlineThresholdInputs();
                updateUnitLabels();
                validateInlineThresholds();

                if (response.presetName) {
                    const presetSpan = document.querySelector('#presetButton span');
                    const presetText = response.presetName === 'PriceSafari' ? 'Presety - Widok PriceSafari' : 'Presety - ' + response.presetName;
                    if (presetSpan) presetSpan.textContent = presetText;
                    else document.getElementById('presetButton').textContent = presetText;
                }

                allPrices = response.prices.map(p => ({ ...p, bucket: p.producerBucket }));

                const validPrices = allPrices.map(i => parseFloat(i.referencePrice)).filter(p => !isNaN(p) && p > 0.01);
                let minP = 0, maxP = 1000;
                if (validPrices.length > 0) { minP = Math.floor(Math.min(...validPrices)); maxP = Math.ceil(Math.max(...validPrices)); }
                if (minP === maxP) maxP = minP + 1;
                myPriceSlider.noUiSlider.updateOptions({ range: { 'min': minP, 'max': maxP } });
                myPriceSlider.noUiSlider.set([minP, maxP]);

                const storeCounts = allPrices.map(i => i.storeCount || 0);
                const maxStoreCount = Math.max(1, ...storeCounts);
                offerSlider.noUiSlider.updateOptions({ range: { 'min': 1, 'max': maxStoreCount } });
                offerSlider.noUiSlider.set([1, maxStoreCount]);

                const positions = allPrices.map(i => i.bestCompetitorPosition).filter(p => p !== null && !isNaN(p));
                const maxPosition = positions.length > 0 ? Math.max(...positions) : 60;
                positionSlider.noUiSlider.updateOptions({ range: { 'min': 1, 'max': maxPosition } });
                positionSlider.noUiSlider.set([1, maxPosition]);

                const totalPriceCountEl = document.getElementById('totalPriceCount');
                if (totalPriceCountEl) totalPriceCountEl.textContent = response.priceCount;

                updateFlagCounts(allPrices);
                updateNewProductCount(allPrices);
                populateProducerFilter();

                massActions.updateSelectionUI();
                filterPricesAndUpdateUI();
            })
            .catch(err => {
                console.error('Błąd loadPrices:', err);
                showGlobalNotification('<p>Wystąpił błąd podczas ładowania danych.</p>');
            })
            .finally(() => hideLoading());
    }
    window.loadPrices = loadPrices;

    function populateInlineThresholdInputs() {
        const v = thresholdsState[currentMode];
        document.getElementById('thrInline_greenDark').value = v.greenDark;
        document.getElementById('thrInline_green').value = v.green;
        document.getElementById('thrInline_greenLight').value = v.greenLight;
        document.getElementById('thrInline_redLight').value = v.redLight;
        document.getElementById('thrInline_red').value = v.red;
        document.getElementById('thrInline_redDark').value = v.redDark;
    }

    function readInlineInputsToState() {
        const get = id => parseFloat((document.getElementById(id).value || '').replace(',', '.'));
        thresholdsState[currentMode] = {
            greenDark: get('thrInline_greenDark'),
            green: get('thrInline_green'),
            greenLight: get('thrInline_greenLight'),
            redLight: get('thrInline_redLight'),
            red: get('thrInline_red'),
            redDark: get('thrInline_redDark')
        };
    }

    function updateUnitLabels() {
        const unit = currentMode === 'amt' ? 'PLN' : '%';
        document.querySelectorAll('.producer-unit-label').forEach(el => el.textContent = unit);
    }

    function validateInlineThresholds() {
        const ids = {
            greenDark: 'thrInline_greenDark', green: 'thrInline_green', greenLight: 'thrInline_greenLight',
            redLight: 'thrInline_redLight', red: 'thrInline_red', redDark: 'thrInline_redDark'
        };
        const v = {
            greenDark: parseFloat((document.getElementById(ids.greenDark).value || '').replace(',', '.')),
            green: parseFloat((document.getElementById(ids.green).value || '').replace(',', '.')),
            greenLight: parseFloat((document.getElementById(ids.greenLight).value || '').replace(',', '.')),
            redLight: parseFloat((document.getElementById(ids.redLight).value || '').replace(',', '.')),
            red: parseFloat((document.getElementById(ids.red).value || '').replace(',', '.')),
            redDark: parseFloat((document.getElementById(ids.redDark).value || '').replace(',', '.'))
        };

        document.querySelectorAll('.producer-threshold-input').forEach(el => el.classList.remove('input-error'));

        const errors = [];
        let isValid = true;

        for (const [k, val] of Object.entries(v)) {
            if (isNaN(val) || val < 0) {
                document.getElementById(ids[k]).classList.add('input-error');
                isValid = false;
            }
        }
        if (!isValid) errors.push('Wszystkie wartości muszą być liczbami nieujemnymi');

        if (!isNaN(v.greenLight) && !isNaN(v.green) && v.greenLight > v.green) {
            document.getElementById(ids.greenLight).classList.add('input-error');
            document.getElementById(ids.green).classList.add('input-error');
            errors.push(`Powyżej: „lekko" (${v.greenLight}) > „normalny" (${v.green})`);
            isValid = false;
        }
        if (!isNaN(v.green) && !isNaN(v.greenDark) && v.green > v.greenDark) {
            document.getElementById(ids.green).classList.add('input-error');
            document.getElementById(ids.greenDark).classList.add('input-error');
            errors.push(`Powyżej: „normalny" (${v.green}) > „mocno" (${v.greenDark})`);
            isValid = false;
        }
        if (!isNaN(v.redLight) && !isNaN(v.red) && v.redLight > v.red) {
            document.getElementById(ids.redLight).classList.add('input-error');
            document.getElementById(ids.red).classList.add('input-error');
            errors.push(`Poniżej: „lekko" (${v.redLight}) > „naruszenie" (${v.red})`);
            isValid = false;
        }
        if (!isNaN(v.red) && !isNaN(v.redDark) && v.red > v.redDark) {
            document.getElementById(ids.red).classList.add('input-error');
            document.getElementById(ids.redDark).classList.add('input-error');
            errors.push(`Poniżej: „naruszenie" (${v.red}) > „drastyczne" (${v.redDark})`);
            isValid = false;
        }

        const errEl = document.getElementById('errInline');
        if (errors.length > 0) {
            errEl.classList.add('visible');
            errEl.innerHTML = errors.map(e => `<i class="fa-solid fa-triangle-exclamation"></i> ${e}`).join('<br>');
        } else {
            errEl.classList.remove('visible');
            errEl.innerHTML = '';
        }

        const saveBtn = document.getElementById('saveProducerInlineBtn');
        if (saveBtn) {
            saveBtn.disabled = !isValid;
            saveBtn.style.opacity = isValid ? '1' : '0.5';
            saveBtn.style.cursor = isValid ? 'pointer' : 'not-allowed';
            saveBtn.title = isValid ? '' : 'Popraw błędy walidacji aby zapisać';
        }

        return isValid;
    }
    document.querySelectorAll('.producer-threshold-input').forEach(el => {
        el.addEventListener('input', validateInlineThresholds);
    });

    document.getElementById('useAmountToggle').addEventListener('change', function () {
        readInlineInputsToState();
        currentMode = this.checked ? 'amt' : 'pct';
        populateInlineThresholdInputs();
        updateUnitLabels();
        validateInlineThresholds();
    });

    document.getElementById('saveProducerInlineBtn').addEventListener('click', function () {
        if (!validateInlineThresholds()) {
            showGlobalNotification('<p style="font-weight:bold;">Walidacja</p><p>Popraw błędy w formularzu progów.</p>');
            return;
        }
        readInlineInputsToState();
        saveAllProducerSettings();
    });

    document.getElementById('openProducerSettingsBtn').addEventListener('click', function () {
        document.getElementById('identifierForSimulationInput').value = producerSettings.identifierForSimulation || 'EAN';
        document.getElementById('comparisonSourceInput').value = producerSettings.comparisonSource;
        $('#producerSettingsModal').modal('show');
    });

    document.getElementById('saveProducerModalBtn').addEventListener('click', function () {
        producerSettings.identifierForSimulation = document.getElementById('identifierForSimulationInput').value;
        producerSettings.comparisonSource = parseInt(document.getElementById('comparisonSourceInput').value);
        readInlineInputsToState();
        saveAllProducerSettings();
        $('#producerSettingsModal').modal('hide');
    });

    function saveAllProducerSettings() {
        const payload = {
            StoreId: storeId,
            IdentifierForSimulation: producerSettings.identifierForSimulation,
            ProducerComparisonSource: producerSettings.comparisonSource,
            ProducerUseAmount: currentMode === 'amt',
            ProducerThresholdRedDarkPercent: thresholdsState.pct.redDark,
            ProducerThresholdRedPercent: thresholdsState.pct.red,
            ProducerThresholdRedLightPercent: thresholdsState.pct.redLight,
            ProducerThresholdGreenLightPercent: thresholdsState.pct.greenLight,
            ProducerThresholdGreenPercent: thresholdsState.pct.green,
            ProducerThresholdGreenDarkPercent: thresholdsState.pct.greenDark,
            ProducerThresholdRedDarkAmount: thresholdsState.amt.redDark,
            ProducerThresholdRedAmount: thresholdsState.amt.red,
            ProducerThresholdRedLightAmount: thresholdsState.amt.redLight,
            ProducerThresholdGreenLightAmount: thresholdsState.amt.greenLight,
            ProducerThresholdGreenAmount: thresholdsState.amt.green,
            ProducerThresholdGreenDarkAmount: thresholdsState.amt.greenDark
        };

        showLoading();
        fetch('/PriceHistory/SaveProducerSettings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        })
            .then(r => r.json())
            .then(data => {
                if (data.success) {
                    showGlobalUpdate('<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Ustawienia zapisane</p><p>Przeliczam dane z nowymi progami...</p>');
                    loadPrices();
                } else {
                    hideLoading();
                    showGlobalNotification('<p>' + (data.message || 'Błąd zapisu') + '</p>');
                }
            })
            .catch(err => {
                console.error(err);
                hideLoading();
                showGlobalNotification('<p>Błąd zapisu ustawień. Sprawdź konsolę.</p>');
            });
    }

    function updateFlagCounts(prices) {
        const flagCounts = {};
        let noFlagCount = 0;
        prices.forEach(p => {
            if (!p.flagIds || p.flagIds.length === 0) noFlagCount++;
            else p.flagIds.forEach(fid => flagCounts[fid] = (flagCounts[fid] || 0) + 1);
        });

        const container = document.getElementById('flagContainer');
        if (!container) return;
        container.innerHTML = '';

        if (typeof flags !== 'undefined' && flags) {
            flags.forEach(flag => {
                const count = flagCounts[flag.flagId] || 0;
                const incChecked = selectedFlagsInclude.has(String(flag.flagId)) ? 'checked' : '';
                const excChecked = selectedFlagsExclude.has(String(flag.flagId)) ? 'checked' : '';
                container.insertAdjacentHTML('beforeend', `
                    <div class="flag-filter-group">
                        <div class="form-check form-check-inline check-include" style="margin-right:0px;">
                            <input class="form-check-input flagFilterInclude" type="checkbox" id="flagCheckboxInclude_${flag.flagId}" value="${flag.flagId}" ${incChecked}>
                        </div>
                        <div class="form-check form-check-inline check-exclude" style="margin-right:0px; padding-left:16px;">
                            <input class="form-check-input flagFilterExclude" type="checkbox" id="flagCheckboxExclude_${flag.flagId}" value="${flag.flagId}" ${excChecked}>
                        </div>
                        <span class="flag-name-count" style="font-size:14px; font-weight:400;">${flag.flagName} (${count})</span>
                    </div>`);
            });
        }

        const nfInc = selectedFlagsInclude.has('noFlag') ? 'checked' : '';
        const nfExc = selectedFlagsExclude.has('noFlag') ? 'checked' : '';
        container.insertAdjacentHTML('beforeend', `
            <div class="flag-filter-group">
                <div class="form-check form-check-inline check-include" style="margin-right:0px;">
                    <input class="form-check-input flagFilterInclude" type="checkbox" id="flagCheckboxInclude_noFlag" value="noFlag" ${nfInc}>
                </div>
                <div class="form-check form-check-inline check-exclude" style="margin-right:0px; padding-left:16px;">
                    <input class="form-check-input flagFilterExclude" type="checkbox" id="flagCheckboxExclude_noFlag" value="noFlag" ${nfExc}>
                </div>
                <span class="flag-name-count" style="font-size:14px; font-weight:400;">Brak flagi (${noFlagCount})</span>
            </div>`);

        document.querySelectorAll('.flagFilterInclude, .flagFilterExclude').forEach(cb => {
            const newCb = cb.cloneNode(true);
            cb.parentNode.replaceChild(newCb, cb);
            newCb.addEventListener('change', function () {
                const val = this.value;
                const isInc = this.classList.contains('flagFilterInclude');
                if (isInc) {
                    if (this.checked) {
                        const exc = document.getElementById(`flagCheckboxExclude_${val}`);
                        if (exc) { exc.checked = false; selectedFlagsExclude.delete(val); }
                        selectedFlagsInclude.add(val);
                    } else selectedFlagsInclude.delete(val);
                } else {
                    if (this.checked) {
                        const inc = document.getElementById(`flagCheckboxInclude_${val}`);
                        if (inc) { inc.checked = false; selectedFlagsInclude.delete(val); }
                        selectedFlagsExclude.add(val);
                    } else selectedFlagsExclude.delete(val);
                }
                filterPricesAndUpdateUI();
            });
        });
    }

    function updateNewProductCount(prices) {
        const newCount = prices.filter(p => p.isNew).length;
        const el = document.getElementById('count-new');
        if (el) el.textContent = `(${newCount})`;
    }

    function updateViolationCounts(prices) {
        let counts = {
            fresh: 0, currentlyViolating: 0, recentlyEnded: 0, noViolations: 0,
            lt1d: 0, d1to3: 0, d3to7: 0, maxWindow: 0
        };

        prices.forEach(p => {
            const hasRef = p.referencePrice != null && p.referencePrice > 0;

            if (p.isCurrentlyViolating) {
                counts.currentlyViolating++;
                if (p.isFreshViolation) counts.fresh++;

                const h = p.violationDurationHours;
                if (p.reachedMaxWindow || (h != null && h >= 168)) counts.maxWindow++;
                else if (h != null && h >= 72) counts.d3to7++;
                else if (h != null && h >= 24) counts.d1to3++;
                else counts.lt1d++;
            } else if (p.wasRecentlyViolated) {
                counts.recentlyEnded++;
            } else if (hasRef) {
                counts.noViolations++;
            }
        });

        const setLabel = (id, text, count) => {
            const el = document.getElementById(id);
            if (el) el.textContent = `${text} (${count})`;
        };

        setLabel('label_freshViolation', 'Nowe naruszenie', counts.fresh);
        setLabel('label_currentlyViolating', 'Obecne naruszenie', counts.currentlyViolating);
        setLabel('label_recentlyEnded', 'Niedawno zakończone', counts.recentlyEnded);
        setLabel('label_noViolations', 'Bez naruszeń (7 dni)', counts.noViolations);

        setLabel('label_violationLt1d', 'Krócej niż 1 dzień', counts.lt1d);
        setLabel('label_violation1to3d', '1 - 3 dni', counts.d1to3);
        setLabel('label_violation3to7d', '3 - 7 dni', counts.d3to7);
        setLabel('label_violationMaxWindow', 'Pełne 7 dni (max)', counts.maxWindow);
    }

    function updateSourceCounts(prices) {
        let g = 0, c = 0;
        prices.forEach(p => {
            if (p.sourceGoogle) g++;
            if (p.sourceCeneo) c++;
        });
        const setLabel = (id, text, count) => {
            const el = document.getElementById(id);
            if (el) el.textContent = `${text} (${count})`;
        };
        setLabel('label_sourceGoogle', 'Google Shopping', g);
        setLabel('label_sourceCeneo', 'Ceneo', c);
    }

    function updateStockCounts(prices) {
        let a = 0, u = 0, nd = 0;
        prices.forEach(p => {
            if (p.bestCompetitorInStock === true) a++;
            else if (p.bestCompetitorInStock === false) u++;
            else nd++;
        });
        const setLabel = (id, text, count) => {
            const el = document.getElementById(id);
            if (el) el.textContent = `${text} (${count})`;
        };
        setLabel('label_compStockAvailable', 'Dostępny', a);
        setLabel('label_compStockUnavailable', 'Niedostępny', u);
        setLabel('label_compStockNoData', 'Brak danych', nd);
    }

    function populateProducerFilter() {
        const dropdown = document.getElementById('producerFilterDropdown');
        const counts = allPrices.reduce((m, i) => { if (i.producer) m[i.producer] = (m[i.producer] || 0) + 1; return m; }, {});
        const producers = Object.keys(counts).sort((a, b) => a.localeCompare(b));
        dropdown.innerHTML = '<option value="">Wszystkie marki</option>';
        producers.forEach(p => {
            const opt = document.createElement('option');
            opt.value = p;
            opt.textContent = `${p} (${counts[p]})`;
            dropdown.appendChild(opt);
        });
    }

    function checkAdvancedCondition(item, name) {
        const h = item.violationDurationHours;
        const hasRef = item.referencePrice != null && item.referencePrice > 0;

        switch (name) {
            case 'isNew': return item.isNew === true;

            case 'freshViolation':
                return item.isFreshViolation === true && item.isCurrentlyViolating === true;
            case 'currentlyViolating':
                return item.isCurrentlyViolating === true;
            case 'recentlyEnded':
                return item.isCurrentlyViolating !== true && item.wasRecentlyViolated === true;
            case 'noViolations':
                return item.isCurrentlyViolating !== true && item.wasRecentlyViolated !== true && hasRef;

            case 'violationLt1d':
                return item.isCurrentlyViolating === true && h != null && h < 24;
            case 'violation1to3d':
                return item.isCurrentlyViolating === true && h != null && h >= 24 && h < 72;
            case 'violation3to7d':
                return item.isCurrentlyViolating === true && h != null && h >= 72 && h < 168 && !item.reachedMaxWindow;
            case 'violationMaxWindow':
                return item.isCurrentlyViolating === true && (item.reachedMaxWindow === true || (h != null && h >= 168));

            case 'sourceGoogle': return item.sourceGoogle === true;
            case 'sourceCeneo': return item.sourceCeneo === true;

            case 'compStockAvailable': return item.bestCompetitorInStock === true;
            case 'compStockUnavailable': return item.bestCompetitorInStock === false;
            case 'compStockNoData': return item.bestCompetitorInStock == null;

            default: return false;
        }
    }

    function filterPricesByCategoryAndColorAndFlag(data) {
        let filtered = data;

        const selBuckets = Array.from(document.querySelectorAll('.bucketFilter:checked')).map(c => c.value);
        if (selBuckets.length) filtered = filtered.filter(item => selBuckets.includes(item.bucket));

        const posVals = positionSlider.noUiSlider.get();
        const posMin = parseInt(posVals[0]), posMax = parseInt(posVals[1]);
        const offVals = offerSlider.noUiSlider.get();
        const offMin = parseInt(offVals[0]), offMax = parseInt(offVals[1]);
        const priceRaw = myPriceSlider.noUiSlider.get();
        const priceMin = parseFloat(priceRaw[0].replace(' PLN', '').replace(/\s/g, '').replace(',', '.'));
        const priceMax = parseFloat(priceRaw[1].replace(' PLN', '').replace(/\s/g, '').replace(',', '.'));

        filtered = filtered.filter(item => {
            const pos = item.bestCompetitorPosition;
            if (pos === null || pos === undefined) return true;
            return parseInt(pos) >= posMin && parseInt(pos) <= posMax;
        });
        filtered = filtered.filter(item => {
            const count = item.storeCount || 0;
            if (count === 0) return true;
            return count >= offMin && count <= offMax;
        });
        filtered = filtered.filter(item => {
            const refP = item.referencePrice != null ? parseFloat(item.referencePrice) : 0;
            if (refP <= 0.01) return true;
            return refP >= priceMin && refP <= priceMax;
        });

        if (selectedFlagsExclude.size > 0) {
            filtered = filtered.filter(item => {
                if (selectedFlagsExclude.has('noFlag') && (!item.flagIds || item.flagIds.length === 0)) return false;
                for (const e of selectedFlagsExclude) if (e !== 'noFlag' && (item.flagIds || []).includes(parseInt(e))) return false;
                return true;
            });
        }
        if (selectedFlagsInclude.size > 0) {
            filtered = filtered.filter(item => {
                if (selectedFlagsInclude.has('noFlag') && (!item.flagIds || item.flagIds.length === 0)) return true;
                return (item.flagIds || []).some(fid => selectedFlagsInclude.has(fid.toString()));
            });
        }

        if (selectedAdvancedExcludes.size > 0)
            filtered = filtered.filter(item => { for (const f of selectedAdvancedExcludes) if (checkAdvancedCondition(item, f)) return false; return true; });
        if (selectedAdvancedIncludes.size > 0)
            filtered = filtered.filter(item => { for (const f of selectedAdvancedIncludes) if (!checkAdvancedCondition(item, f)) return false; return true; });

        return filtered;
    }

    function filterPricesAndUpdateUI(resetPageFlag = true) {
        if (resetPageFlag) { currentPage = 1; window.scrollTo(0, 0); }
        showLoading();

        setTimeout(() => {
            let filtered = [...allPrices];

            const productSearch = document.getElementById('productSearch').value.trim();
            const storeSearch = document.getElementById('storeSearch').value.trim();

            if (productSearch) {
                const sanitized = productSearch.replace(/[^a-zA-Z0-9\s.-]/g, '').toLowerCase().replace(/\s+/g, '');
                filtered = filtered.filter(p => {
                    let id = '';
                    switch (producerSettings.identifierForSimulation) {
                        case 'ID': id = p.externalId ? p.externalId.toString() : ''; break;
                        case 'ProducerCode': id = p.producerCode || ''; break;
                        default: id = p.ean || ''; break;
                    }
                    const combo = ((p.productName || '') + ' ' + id).toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
                    return combo.includes(sanitized);
                });
            }

            if (storeSearch) {
                const sanitized = storeSearch.replace(/[^a-zA-Z0-9\s.-]/g, '').toLowerCase().replace(/\s+/g, '');
                filtered = filtered.filter(p => {
                    const sn = (p.bestCompetitorStoreName || '').toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
                    return sn.includes(sanitized);
                });
            }

            filtered = filterPricesByCategoryAndColorAndFlag(filtered);

            if (sortingState.sortName !== null) {
                filtered.sort((a, b) => sortingState.sortName === 'asc' ? a.productName.localeCompare(b.productName) : b.productName.localeCompare(a.productName));
            } else if (sortingState.sortPrice !== null) {
                filtered.sort((a, b) => sortingState.sortPrice === 'asc' ? (b.referencePrice ?? -Infinity) - (a.referencePrice ?? -Infinity) : (a.referencePrice ?? Infinity) - (b.referencePrice ?? Infinity));
            } else if (sortingState.sortDeltaPercent !== null) {
                filtered.sort((a, b) => sortingState.sortDeltaPercent === 'asc' ? (b.deltaPercent ?? -Infinity) - (a.deltaPercent ?? -Infinity) : (a.deltaPercent ?? Infinity) - (b.deltaPercent ?? Infinity));
            } else if (sortingState.sortDeltaAmount !== null) {
                filtered.sort((a, b) => sortingState.sortDeltaAmount === 'asc' ? (b.deltaAbsolute ?? -Infinity) - (a.deltaAbsolute ?? -Infinity) : (a.deltaAbsolute ?? Infinity) - (b.deltaAbsolute ?? Infinity));
            } else if (sortingState.sortViolationDuration !== null) {
                filtered = filtered.filter(i => i.violationDurationHours !== null && i.violationDurationHours !== undefined);
                filtered.sort((a, b) => sortingState.sortViolationDuration === 'asc'
                    ? (a.violationDurationHours ?? Infinity) - (b.violationDurationHours ?? Infinity)
                    : (b.violationDurationHours ?? -Infinity) - (a.violationDurationHours ?? -Infinity));
            } else if (sortingState.sortStoresViolating !== null) {
                filtered.sort((a, b) => sortingState.sortStoresViolating === 'asc' ? (a.storesBelowReference || 0) - (b.storesBelowReference || 0) : (b.storesBelowReference || 0) - (a.storesBelowReference || 0));
            } else if (sortingState.sortCeneoSales !== null) {
                filtered = filtered.filter(i => i.ceneoSalesCount !== null);
                filtered.sort((a, b) => sortingState.sortCeneoSales === 'asc' ? (b.ceneoSalesCount ?? -Infinity) - (a.ceneoSalesCount ?? -Infinity) : (a.ceneoSalesCount ?? Infinity) - (b.ceneoSalesCount ?? Infinity));
            } else if (sortingState.sortSalesTrendAmount !== null) {
                filtered = filtered.filter(i => i.salesDifference !== null);
                filtered.sort((a, b) => sortingState.sortSalesTrendAmount === 'asc' ? (b.salesDifference ?? -Infinity) - (a.salesDifference ?? -Infinity) : (a.salesDifference ?? Infinity) - (b.salesDifference ?? Infinity));
            } else if (sortingState.sortSalesTrendPercent !== null) {
                filtered = filtered.filter(i => i.salesPercentageChange !== null);
                filtered.sort((a, b) => sortingState.sortSalesTrendPercent === 'asc' ? (b.salesPercentageChange ?? -Infinity) - (a.salesPercentageChange ?? -Infinity) : (a.salesPercentageChange ?? Infinity) - (b.salesPercentageChange ?? Infinity));
            }

            const selProducer = document.getElementById('producerFilterDropdown').value;
            if (selProducer) filtered = filtered.filter(i => i.producer === selProducer);

            currentlyFilteredPrices = [...filtered];

            renderPrices(filtered);
            renderChart(filtered);
            updateBucketCountsUI(filtered);
            updateFlagCounts(filtered);
            updateViolationCounts(filtered);
            updateSourceCounts(filtered);
            updateStockCounts(filtered);
            updateNewProductCount(filtered);
            hideLoading();
        }, 0);
    }

    function renderPagination(totalItems) {
        const totalPages = Math.ceil(totalItems / itemsPerPage);
        const c = document.getElementById('paginationContainer');
        c.innerHTML = '';
        if (totalPages <= 1) return;

        const prev = document.createElement('button');
        prev.innerHTML = '<i class="fa fa-chevron-circle-left"></i>';
        prev.disabled = currentPage === 1;
        prev.addEventListener('click', () => { if (currentPage > 1) { currentPage--; filterPricesAndUpdateUI(false); } });
        c.appendChild(prev);

        const max = 5;
        let start = Math.max(1, currentPage - Math.floor(max / 2));
        let end = start + max - 1;
        if (end > totalPages) { end = totalPages; start = Math.max(1, end - max + 1); }
        if (start > 1) {
            const fb = document.createElement('button');
            fb.textContent = '1';
            fb.addEventListener('click', () => { currentPage = 1; filterPricesAndUpdateUI(false); });
            c.appendChild(fb);
            if (start > 2) { const d = document.createElement('span'); d.innerHTML = '&hellip;'; d.style.margin = '0 5px'; c.appendChild(d); }
        }
        for (let i = start; i <= end; i++) {
            const b = document.createElement('button');
            b.textContent = i;
            if (i === currentPage) b.classList.add('active');
            b.addEventListener('click', () => { currentPage = i; filterPricesAndUpdateUI(false); });
            c.appendChild(b);
        }
        if (end < totalPages) {
            if (end < totalPages - 1) { const d = document.createElement('span'); d.innerHTML = '&hellip;'; d.style.margin = '0 5px'; c.appendChild(d); }
            const lb = document.createElement('button');
            lb.textContent = totalPages;
            lb.addEventListener('click', () => { currentPage = totalPages; filterPricesAndUpdateUI(false); });
            c.appendChild(lb);
        }

        const next = document.createElement('button');
        next.innerHTML = '<i class="fa fa-chevron-circle-right"></i>';
        next.disabled = currentPage === totalPages;
        next.addEventListener('click', () => { if (currentPage < totalPages) { currentPage++; filterPricesAndUpdateUI(false); } });
        c.appendChild(next);
    }

    function createFlagsContainer(item) {
        const c = document.createElement('div');
        c.className = 'flags-container';
        if (item.flagIds && item.flagIds.length > 0) {
            item.flagIds.forEach(fid => {
                const f = flags.find(x => x.flagId === fid);
                if (f) {
                    const s = document.createElement('span');
                    s.className = 'flag';
                    s.style.color = f.flagColor;
                    s.style.border = '2px solid ' + f.flagColor;
                    s.style.backgroundColor = hexToRgba(f.flagColor, 0.3);
                    s.innerHTML = f.flagName;
                    c.appendChild(s);
                }
            });
        }
        return c;
    }

    function buildStatsBlocks(item) {
        const blocks = [];

        blocks.push(`
        <div class="price-box-column-offers-a">
            <span class="data-channel">
                ${item.sourceGoogle ? '<img src="/images/GoogleShopping.png" alt="Google Shopping" style="width:15px;height:15px;" />' : ''}
                ${item.sourceCeneo ? '<img src="/images/Ceneo.png" alt="Ceneo" style="width:15px;height:15px;" />' : ''}
            </span>
            <div class="offer-count-box">${getOfferText(item.storeCount || 0)}</div>
        </div>`);

        let positionHtml = '';

        if (producerSettings.comparisonSource === 1) {
            if (item.simulatedPosition && !item.simulatedPosition.includes("N/A / 0")) {
                positionHtml = `
            <div class="price-box-column-offers-a" title="Symulowana pozycja dla katalogowej ceny MAP">
                <span class="data-channel">
                    <i class="fas fa-trophy" style="font-size: 15px; color: #9D4EDD; margin-top:1px;"></i>
                </span>
                <div class="offer-count-box">
                    <p style="color: #9D4EDD; font-weight: 600;">Symulacja: ${item.simulatedPosition}</p>
                </div>
            </div>`;
            } else {
                positionHtml = `
            <div class="price-box-column-offers-a" title="Brak ceny MAP do symulacji">
                <span class="data-channel">
                    <i class="fas fa-trophy" style="font-size: 15px; color: grey; margin-top:1px;"></i>
                </span>
                <div class="offer-count-box"><p>Brak MAP</p></div>
            </div>`;
            }
        }
        else {
            if (item.myPricePosition && !item.myPricePosition.includes("N/A")) {
                positionHtml = `
            <div class="price-box-column-offers-a" title="Rzeczywista pozycja Twojej oferty">
                <span class="data-channel">
                    <i class="fas fa-trophy" style="font-size: 15px; color: grey; margin-top:1px;"></i>
                </span>
                <div class="offer-count-box"><p>${item.myPricePosition}</p></div>
            </div>`;
            } else {
                positionHtml = `
            <div class="price-box-column-offers-a" title="Brak Twojej oferty">
                <span class="data-channel">
                    <i class="fas fa-trophy" style="font-size: 15px; color: grey; margin-top:1px;"></i>
                </span>
                <div class="offer-count-box"><p>Brak oferty</p></div>
            </div>`;
            }
        }
        blocks.push(positionHtml);

        if (item.ceneoSalesCount != null && item.ceneoSalesCount > 0) {
            blocks.push(`
            <div class="price-box-column-offers-a" title="Sprzedaż Ceneo (90 dni)">
                <span class="data-channel"><i class="fas fa-shopping-cart" style="font-size:15px; color:grey; margin-top:1px;"></i></span>
                <div class="offer-count-box"><p>${item.ceneoSalesCount} szt.</p></div>
            </div>`);
        } else {
            blocks.push(`
            <div class="price-box-column-offers-a" title="Sprzedaż Ceneo (90 dni)">
                <span class="data-channel"><i class="fas fa-shopping-cart" style="font-size:15px; color:grey; margin-top:1px;"></i></span>
                <div class="offer-count-box"><p>Brak sprzedaży</p></div>
            </div>`);
        }

        if (item.salesTrendStatus && item.salesTrendStatus !== 'NoData') {
            let trendText = 'Bez zmian';
            if (item.salesDifference !== 0 && item.salesDifference !== null) {
                const sign = item.salesDifference > 0 ? '+' : '';
                const pct = item.salesPercentageChange !== null ? ` (${sign}${item.salesPercentageChange.toFixed(1)}%)` : '';
                trendText = `${sign}${item.salesDifference}${pct}`;
            }
            blocks.push(`
            <div class="price-box-column-offers-a" title="Trend sprzedaży">
                <span class="data-channel"><img src="/images/Flag-${item.salesTrendStatus}.svg" alt="" style="width:18px;height:18px;" /></span>
                <div class="offer-count-box"><p>${trendText}</p></div>
            </div>`);
        } else {
            blocks.push(`
            <div class="price-box-column-offers-a" title="Trend sprzedaży">
                <span class="data-channel"><i class="fas fa-chart-line" style="font-size:14px; color:grey;"></i></span>
                <div class="offer-count-box"><p>Brak danych</p></div>
            </div>`);
        }

        return blocks.join('');
    }

    function buildCompetitorBox(item, storeSearchTerm) {
        const bestComp = item.bestCompetitorPrice != null ? parseFloat(item.bestCompetitorPrice) : null;
        const highlightedStoreName = highlightMatches(item.bestCompetitorStoreName || '', storeSearchTerm);

        if (bestComp == null) {
            return `
                <div class="price-box-column">
                    <div class="price-box-column-text">
                        <span style="font-weight:500; font-size:17px;">Brak konkurencji</span>
                        <div style="color:#444; font-size:13px;">Produkt nie ma ofert na rynku</div>
                    </div>
                    <div class="price-box-column-text">
                        <span class="Position-Producer">Brak ofert</span>
                    </div>
                </div>`;
        }

        const channelIcon = item.bestCompetitorIsGoogle != null
            ? `<img src="${item.bestCompetitorIsGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png'}" alt="" style="width:14px; height:14px; margin-right:4px;" />`
            : '';

        let positionBadge = '';
        if (item.bestCompetitorPosition != null) {
            const positionClass = item.bestCompetitorIsGoogle ? 'Position-Google' : 'Position';
            const positionLabel = item.bestCompetitorIsGoogle ? 'Poz. Google' : 'Poz. Ceneo';
            positionBadge = `<span class="${positionClass}">${positionLabel} ${item.bestCompetitorPosition}</span>`;
        }

        let biddingBadge = '';
        if (item.bestCompetitorIsBidding === true || item.bestCompetitorIsBidding === 'true' || item.bestCompetitorIsBidding === 'Bid') {
            biddingBadge = '<span class="Bidding">Bid</span>';
        }

        const stockBadge = getStockBadge(item.bestCompetitorInStock);

        return `
            <div class="price-box-column">
                <div class="price-box-column-text">
                    <div style="display:flex; align-items:center;">
                        <span style="font-weight:500; font-size:17px;">${formatPricePL(bestComp)}</span>
                    </div>
                    <div style="display:flex; align-items:center; gap:4px; color:#444; font-size:13px;">
                        ${channelIcon}<span>${highlightedStoreName}</span>
                    </div>
                </div>
                <div class="price-box-column-text" style="display:flex; gap:4px; flex-wrap:wrap;">
                    ${positionBadge}${stockBadge}${biddingBadge}
                </div>
            </div>`;
    }

    function buildReferenceBox(item) {
        const refPrice = item.referencePrice != null ? parseFloat(item.referencePrice) : null;
        const myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
        const mapPrice = item.mapPrice != null ? parseFloat(item.mapPrice) : null;

        if (refPrice != null && refPrice > 0) {
            const sourceLabel = item.referenceSource === 'map' ? 'Cena MAP' : 'Cena Twojego sklepu';

            const altLines = [];
            if (item.referenceSource === 'map' && myPrice != null && myPrice > 0) {
                altLines.push(`<div class="ref-alt-line">Twoja oferta: <strong>${formatPricePL(myPrice)}</strong></div>`);
            }
            if (item.referenceSource === 'store' && mapPrice != null && mapPrice > 0) {
                altLines.push(`<div class="ref-alt-line">MAP w katalogu: <strong>${formatPricePL(mapPrice)}</strong></div>`);
            }

            let myStockBadge = '';
            if (item.referenceSource === 'store' && myPrice != null && myPrice > 0) {
                myStockBadge = `<div style="margin-top:4px;">${getStockBadge(item.myEntryInStock)}</div>`;
            }

            return `
                <div class="price-box-column">
                    <div class="price-box-column-text">
                        <div><span class="ref-label-small">${sourceLabel}</span></div>
                        <div><span style="font-weight:500; font-size:17px;">${formatPricePL(refPrice)}</span></div>
                        ${item.referenceSource === 'store' ? `<div style="color:#444; font-size:13px;"><span>${myStoreName || ''}</span></div>` : ''}
                        ${altLines.join('')}
                    </div>
                    <div class="price-box-column-text">
                        ${myStockBadge}
                    </div>
                </div>`;
        }

        let missingMsg = '';
        if (producerSettings.comparisonSource === 1) missingMsg = 'Brak ceny MAP w katalogu';
        else missingMsg = 'Brak Twojej oferty';

        const altLines = [];
        if (mapPrice != null && mapPrice > 0) altLines.push(`<div class="ref-alt-line">MAP w katalogu: <strong>${formatPricePL(mapPrice)}</strong></div>`);
        if (myPrice != null && myPrice > 0) altLines.push(`<div class="ref-alt-line">Twoja oferta: <strong>${formatPricePL(myPrice)}</strong></div>`);

        return `
            <div class="price-box-column">
                <div class="price-box-column-text">
                    <div><span class="ref-label-small">Cena referencyjna</span></div>
                    <div class="ref-missing"><i class="fa-solid fa-circle-exclamation"></i> ${missingMsg}</div>
                    ${altLines.join('')}
                </div>
                <div class="price-box-column-text"></div>
            </div>`;
    }

    function buildViolationDetailBox(item) {
        const hasRef = item.referencePrice != null && parseFloat(item.referencePrice) > 0;

        if (!hasRef) {
            return `
                <div class="price-box-column">
                    <div class="price-box-column-text">
                        <div class="violation-detail-box">
                            <div class="violation-detail-header">
                                <i class="violation-detail-icon fa-solid fa-circle-question" style="color:#5b537a;"></i>
                                <span>Brak ceny referencyjnej</span>
                            </div>
                            <div class="violation-detail-meta">Nie można ocenić naruszeń.</div>
                        </div>
                    </div>
                    <div class="price-box-column-text">
                        <span class="violation-state-badge violation-state-no-ref">Brak ref.</span>
                    </div>
                </div>`;
        }

        if (item.isCurrentlyViolating) {
            const isFresh = item.isFreshViolation === true;
            const reachedMax = item.reachedMaxWindow === true;
            const h = item.violationDurationHours;

            let stateBadgeClass, stateBadgeText, iconClass, iconColor, headerText, durationText, metaHtml;

            if (isFresh) {
                stateBadgeClass = 'violation-state-fresh';
                stateBadgeText = 'Nowe';
                iconClass = 'fa-solid fa-bell';
                iconColor = '#9D4EDD';
                headerText = 'Nowe naruszenie';
                durationText = 'Wykryto ostatnio';
                metaHtml = '<div class="violation-detail-meta">Pierwsze wykrycie.</div>';
            } else {
                stateBadgeClass = 'violation-state-active';
                stateBadgeText = 'Aktywne';
                iconClass = 'fa-solid fa-triangle-exclamation';
                iconColor = '#8b1a1a';
                headerText = 'Trwające naruszenie';
                durationText = formatViolationDuration(h, reachedMax);
                metaHtml = reachedMax
                    ? '<div class="violation-detail-meta">Bez przerwy ≥ 7 dni.</div>'
                    : '<div class="violation-detail-meta">Trwa nieprzerwanie.</div>';
            }

            const barPercent = isFresh
                ? 2
                : (reachedMax ? 100 : Math.min(100, ((h || 0) / 168) * 100));

            let barFillColor = '#f4d03f';
            if (!isFresh) {
                if (barPercent >= 75) barFillColor = '#c0392b';
                else if (barPercent >= 35) barFillColor = '#f39c12';
                else barFillColor = '#f4d03f';
            }

            return `
                <div class="price-box-column">
                    <div class="price-box-column-text">
                        <div class="violation-detail-box">
                            <div class="violation-detail-header">
                                <i class="violation-detail-icon ${iconClass}" style="color:${iconColor};"></i>
                                <span>${headerText}</span>
                            </div>
                            <div class="violation-duration-large">${durationText}</div>
                            ${metaHtml}
                            <div class="violation-time-bar" title="Postęp w oknie 7 dni">
                                <div class="violation-time-bar-fill" style="width:${barPercent.toFixed(1)}%; background-color:${barFillColor};"></div>
                            </div>
                        </div>
                    </div>
                    <div class="price-box-column-text">
                        <span class="violation-state-badge ${stateBadgeClass}">${stateBadgeText}</span>
                    </div>
                </div>`;
        }

        if (item.wasRecentlyViolated) {
            const endedAgo = formatHoursAgo(item.lastViolationEndedHoursAgo);
            const dur = formatDurationShort(item.lastViolationDurationHours);

            return `
                <div class="price-box-column">
                    <div class="price-box-column-text">
                        <div class="violation-detail-box">
                            <div class="violation-detail-header">
                                <i class="violation-detail-icon fa-solid fa-clock-rotate-left" style="color:#495057;"></i>
                                <span>Niedawno zakończone</span>
                            </div>
                            <div class="violation-detail-meta">Trwało: <strong>${dur}</strong></div>
                            <div class="violation-detail-meta">Zakończone: <strong>${endedAgo} temu</strong></div>
                        </div>
                    </div>
                    <div class="price-box-column-text">
                        <span class="violation-state-badge violation-state-ended">Zakończone</span>
                    </div>
                </div>`;
        }

        return `
            <div class="price-box-column">
                <div class="price-box-column-text">
                    <div class="violation-detail-box">
                        <div class="violation-detail-header">
                            <i class="violation-detail-icon fa-solid fa-shield-halved" style="color:#1e5a2a;"></i>
                            <span>Bez naruszeń</span>
                        </div>
                        <div class="violation-detail-meta">Brak naruszeń ceny w ostatnich 7 dniach.</div>
                    </div>
                </div>
                <div class="price-box-column-text">
                    <span class="violation-state-badge violation-state-clean">Czysto</span>
                </div>
            </div>`;
    }

    function buildDeltaBox(item) {
        if (item.bucket === 'producer-no-reference') {
            return `
                <div class="price-box-column">
                    <div class="price-box-column-text">
                        <span style="color:#888; font-style:italic; font-size:13px;">Nie można obliczyć</span>
                        <div style="font-size:12px; color:#aaa;">Brak ceny referencyjnej</div>
                    </div>
                    <div class="price-box-column-text"></div>
                </div>`;
        }
        if (item.bucket === 'producer-no-competition') {
            return `
                <div class="price-box-column">
                    <div class="price-box-column-text">
                        <span style="color:#888; font-style:italic; font-size:13px;">Brak danych</span>
                        <div style="font-size:12px; color:#aaa;">Brak ofert konkurencji</div>
                    </div>
                    <div class="price-box-column-text"></div>
                </div>`;
        }

        const deltaA = item.deltaAbsolute != null ? parseFloat(item.deltaAbsolute) : 0;
        const deltaP = item.deltaPercent != null ? parseFloat(item.deltaPercent) : null;

        let arrow = '<i class="fa-solid fa-equals"></i>';
        let rowClass = 'neutral';

        if (deltaA < 0) {
            arrow = '<i class="fa-solid fa-arrow-down"></i>';
            rowClass = (item.bucket === 'producer-deep-violation') ? 'deep-bad' : 'bad';
        } else if (deltaA > 0) {
            arrow = '<i class="fa-solid fa-arrow-up"></i>';
            rowClass = (item.bucket === 'producer-deep-above') ? 'deep-good' : 'good';
        }

        const sign = deltaA > 0 ? '+' : (deltaA < 0 ? '' : '');
        const pctSign = deltaP > 0 ? '+' : '';

        const bgClassMap = {
            'producer-deep-violation': 'producer-bg-deep-violation',
            'producer-violation': 'producer-bg-violation',
            'producer-minor-below': 'producer-bg-minor-below',
            'producer-equal': 'producer-bg-equal',
            'producer-minor-above': 'producer-bg-minor-above',
            'producer-above': 'producer-bg-above',
            'producer-deep-above': 'producer-bg-deep-above'
        };
        const bgClass = bgClassMap[item.bucket] || 'producer-bg-equal';
        const bucketLabel = BUCKET_BADGE_LABELS[item.bucket] || BUCKET_LABELS[item.bucket] || '';

        let storesDistHtml = '';
        const lines = [];
        if ((item.storesBelowReference || 0) > 0) {
            lines.push(`<div class="producer-stores-line bad"><i class="fa-solid fa-arrow-down"></i> ${item.storesBelowReference} łamie cenę ref.</div>`);
        }
        if ((item.storesAtReference || 0) > 0) {
            lines.push(`<div class="producer-stores-line equal"><i class="fa-solid fa-equals"></i> ${item.storesAtReference} zgodnych z ref.</div>`);
        }
        if ((item.storesAboveReference || 0) > 0) {
            lines.push(`<div class="producer-stores-line good"><i class="fa-solid fa-arrow-up"></i> ${item.storesAboveReference} powyżej ref.</div>`);
        }
        if (lines.length > 0) {
            storesDistHtml = `<div class="producer-stores-dist" style="margin-top:6px;">${lines.join('')}</div>`;
        }

        return `
            <div class="price-box-column">
                <div class="price-box-column-text">
                    <div class="producer-delta-value-row ${rowClass}">
                        ${arrow}
                        <span style="font-weight:500; font-size:17px;">${sign}${formatPricePL(Math.abs(deltaA), false)} PLN</span>
                    </div>
                    ${deltaP != null ? `<div style="font-size:13px; color:#555; margin-top:2px;">${pctSign}${deltaP.toFixed(2)}% wzgl. ceny ref.</div>` : ''}
                    ${storesDistHtml}
                </div>
                <div class="price-box-column-text">
                    <span class="producer-delta-badge ${bgClass}">${bucketLabel}</span>
                </div>
            </div>`;
    }

    function renderPrices(data) {
        const c = document.getElementById('priceContainer');
        const productSearchTerm = document.getElementById('productSearch').value.trim();
        const storeSearchTerm = document.getElementById('storeSearch').value.trim();
        c.innerHTML = '';

        const start = (currentPage - 1) * itemsPerPage;
        const end = currentPage * itemsPerPage;
        const paginated = data.slice(start, end);

        const fragment = document.createDocumentFragment();

        paginated.forEach(item => {
            const highlightedName = highlightMatches(item.productName, productSearchTerm);

            const box = document.createElement('div');
            box.className = 'price-box';
            box.dataset.detailsUrl = `/PriceHistory/Details?scrapId=${currentScrapId}&productId=${item.productId}`;
            box.dataset.productId = item.productId;
            box.dataset.productName = item.productName;
            box.addEventListener('click', function (event) {
                if (event.target.closest('button, a, img, .select-product-btn, .ApiBox')) return;
                window.open(this.dataset.detailsUrl, '_blank');
            });

            const priceBoxSpace = document.createElement('div');
            priceBoxSpace.className = 'price-box-space';

            const leftCol = document.createElement('div');
            leftCol.className = 'price-box-left-column';

            const nameDiv = document.createElement('div');
            nameDiv.className = 'price-box-column-name';

            let colorVariantHtml = '';
            if (item.googleColor && item.googleColor.trim() !== '') {
                colorVariantHtml = `<span style="background-color:#000;color:#fff;border-radius:5px;padding:2px 6px;font-size:12px;margin-left:6px;display:inline-block;vertical-align:middle;">${item.googleColor}</span>`;
            }
            nameDiv.innerHTML = highlightedName + colorVariantHtml;

            if (item.isNew) {
                nameDiv.insertAdjacentHTML('beforeend',
                    `<div style="display:flex; align-items:center; gap:5px; margin-top:3px; flex-wrap:wrap;">
                        <span class="badge-new">NEW</span>
                    </div>`
                );
            }

            leftCol.appendChild(nameDiv);
            leftCol.appendChild(createFlagsContainer(item));

            const rightCol = document.createElement('div');
            rightCol.className = 'price-box-right-column';

            const selBtn = document.createElement('button');
            selBtn.className = 'select-product-btn';
            selBtn.dataset.productId = item.productId;
            selBtn.style.pointerEvents = 'auto';
            if (massActions.isSelected(item.productId.toString())) { selBtn.textContent = 'Wybrano'; selBtn.classList.add('selected'); }
            else { selBtn.textContent = 'Zaznacz'; }
            selBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                const pid = this.dataset.productId;
                massActions.toggleProduct(pid);
                if (massActions.isSelected(pid)) { this.textContent = 'Wybrano'; this.classList.add('selected'); }
                else { this.textContent = 'Zaznacz'; this.classList.remove('selected'); }
            });

            const apiBox = document.createElement('span');
            apiBox.className = 'ApiBox';
            let idVal, idLabel;
            switch (producerSettings.identifierForSimulation) {
                case 'ID': idVal = item.externalId ? item.externalId.toString() : null; idLabel = 'ID'; break;
                case 'ProducerCode': idVal = item.producerCode || null; idLabel = 'SKU'; break;
                default: idVal = item.ean || null; idLabel = 'EAN'; break;
            }
            if (idVal) {
                apiBox.innerHTML = `${idLabel} ${highlightMatches(idVal.toString(), productSearchTerm, 'highlighted-text-yellow')}`;
                apiBox.style.cursor = 'pointer';
                apiBox.title = 'Kliknij, aby skopiować';
                apiBox.addEventListener('click', function (e) {
                    e.stopPropagation();
                    navigator.clipboard.writeText(idVal.toString()).then(() => {
                        const orig = apiBox.innerHTML, origBg = apiBox.style.backgroundColor, origC = apiBox.style.color;
                        apiBox.innerHTML = 'Skopiowano!'; apiBox.style.backgroundColor = '#198754'; apiBox.style.color = 'white';
                        setTimeout(() => { apiBox.innerHTML = orig; apiBox.style.backgroundColor = origBg; apiBox.style.color = origC; }, 2000);
                    });
                });
            } else {
                apiBox.innerHTML = `Brak ${idLabel}`;
            }
            rightCol.appendChild(selBtn);
            rightCol.appendChild(apiBox);

            priceBoxSpace.appendChild(leftCol);
            priceBoxSpace.appendChild(rightCol);

            const priceBoxData = document.createElement('div');
            priceBoxData.className = 'price-box-data';

            const colorBar = document.createElement('div');
            colorBar.className = 'color-bar ' + (item.bucket || 'producer-no-reference');
            priceBoxData.appendChild(colorBar);

            if (item.imgUrl) {
                const img = document.createElement('img');
                img.dataset.src = item.imgUrl;
                img.alt = item.productName;
                img.className = 'lazy-load';
                img.style.cssText = 'width:142px; height:182px; object-fit:contain; object-position:center; margin-right:3px; margin-left:3px; background-color:#ffffff; border:1px solid #e3e3e3; border-radius:4px; padding:8px; display:block;';
                priceBoxData.appendChild(img);
            }

            const stats = document.createElement('div');
            stats.className = 'price-box-stats-container';
            stats.innerHTML = buildStatsBlocks(item);
            priceBoxData.appendChild(stats);

            priceBoxData.insertAdjacentHTML('beforeend', buildCompetitorBox(item, storeSearchTerm));
            priceBoxData.insertAdjacentHTML('beforeend', buildReferenceBox(item));
            priceBoxData.insertAdjacentHTML('beforeend', buildViolationDetailBox(item));
            priceBoxData.insertAdjacentHTML('beforeend', buildDeltaBox(item));

            box.appendChild(priceBoxSpace);
            box.appendChild(priceBoxData);
            fragment.appendChild(box);
        });

        c.appendChild(fragment);
        renderPagination(data.length);

        const lazyImgs = c.querySelectorAll('.lazy-load');
        const obs = new IntersectionObserver((entries, observer) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    const img = entry.target;
                    img.src = img.dataset.src;
                    img.onload = () => img.classList.add('loaded');
                    img.onerror = () => { img.src = '/images/no-image.png'; };
                    observer.unobserve(img);
                }
            });
        }, { rootMargin: '100px' });
        lazyImgs.forEach(im => obs.observe(im));

        document.getElementById('displayedProductCount').textContent = `${data.length} / ${allPrices.length}`;
    }

    function renderChart(data) {
        const ctx = document.getElementById('colorChart').getContext('2d');

        const counts = {};
        BUCKETS_ORDERED.forEach(b => counts[b.key] = 0);
        data.forEach(i => { if (counts.hasOwnProperty(i.bucket)) counts[i.bucket]++; });

        const labels = BUCKETS_ORDERED.map(b => b.label);
        const values = BUCKETS_ORDERED.map(b => counts[b.key]);
        const colors = BUCKETS_ORDERED.map(b => b.color);

        if (chartInstance) {
            chartInstance.data.labels = labels;
            chartInstance.data.datasets[0].data = values;
            chartInstance.data.datasets[0].backgroundColor = colors;
            chartInstance.data.datasets[0].borderColor = colors;
            chartInstance.update();
        } else {
            chartInstance = new Chart(ctx, {
                type: 'doughnut',
                data: { labels: labels, datasets: [{ data: values, backgroundColor: colors, borderColor: colors, borderWidth: 1 }] },
                options: {
                    aspectRatio: 1, cutout: '65%', layout: { padding: 4 },
                    plugins: {
                        legend: { display: false },
                        tooltip: { callbacks: { label: c => c.label + ': ' + c.parsed } }
                    }
                }
            });
        }
    }

    function updateBucketCountsUI(data) {
        const counts = {};
        BUCKETS_ORDERED.forEach(b => counts[b.key] = 0);
        data.forEach(i => { if (counts.hasOwnProperty(i.bucket)) counts[i.bucket]++; });

        for (const [bucketKey, eid] of Object.entries(BUCKET_TO_CHECKBOX)) {
            const lbl = document.querySelector(`label[for="${eid}"]`);
            if (lbl) {
                const txt = lbl.textContent.split('(')[0].trim();
                lbl.textContent = `${txt} (${counts[bucketKey] || 0})`;
            }
        }
    }

    function resetSortingStates(except) {
        Object.keys(sortingState).forEach(k => { if (k !== except) sortingState[k] = null; });
        updateSortButtonVisuals();
    }

    function getDefaultButtonLabel(key) {
        switch (key) {
            case 'sortName': return 'A-Z';
            case 'sortPrice': return 'Cena ref.';
            case 'sortDeltaPercent': return 'Delta %';
            case 'sortDeltaAmount': return 'Delta PLN';
            case 'sortViolationDuration': return 'Czas naruszenia';
            case 'sortStoresViolating': return 'Liczba naruszycieli';
            case 'sortCeneoSales': return 'Sprzedaż - ilość';
            case 'sortSalesTrendAmount': return 'Trend - ilość';
            case 'sortSalesTrendPercent': return 'Trend - %';
            default: return '';
        }
    }

    function updateSortButtonVisuals() {
        Object.keys(sortingState).forEach(k => {
            const v = sortingState[k];
            const btn = document.getElementById(k);
            if (!btn) return;
            if (v !== null) {
                btn.classList.add('active');
                btn.innerHTML = getDefaultButtonLabel(k) + (v === 'asc' ? ' ↑' : ' ↓');
            } else {
                btn.classList.remove('active');
                btn.innerHTML = getDefaultButtonLabel(k);
            }
        });
    }

    function bindSortButton(id) {
        const btn = document.getElementById(id);
        if (!btn) return;
        btn.addEventListener('click', function () {
            const cur = sortingState[id];
            if (cur === null) sortingState[id] = 'asc';
            else if (cur === 'asc') sortingState[id] = 'desc';
            else sortingState[id] = null;
            resetSortingStates(id);
            localStorage.setItem('priceHistoryProducerSorting_' + storeId, JSON.stringify(sortingState));
            filterPricesAndUpdateUI();
        });
    }

    ['sortName', 'sortPrice', 'sortDeltaPercent', 'sortDeltaAmount',
        'sortViolationDuration', 'sortStoresViolating',
        'sortCeneoSales', 'sortSalesTrendAmount', 'sortSalesTrendPercent']
        .forEach(bindSortButton);

    const storedSort = localStorage.getItem('priceHistoryProducerSorting_' + storeId);
    if (storedSort) {
        try {
            const parsed = JSON.parse(storedSort);
            const validKeys = Object.keys(sortingState);
            const cleaned = {};
            validKeys.forEach(k => { if (parsed[k] !== undefined) cleaned[k] = parsed[k]; });
            sortingState = { ...sortingState, ...cleaned };
            updateSortButtonVisuals();
        } catch (e) {
            localStorage.removeItem('priceHistoryProducerSorting_' + storeId);
        }
    }

    const debouncedFilter = debounce(() => filterPricesAndUpdateUI(), 300);
    document.getElementById('productSearch').addEventListener('input', debouncedFilter);
    document.getElementById('storeSearch').addEventListener('input', debouncedFilter);
    document.getElementById('producerFilterDropdown').addEventListener('change', () => filterPricesAndUpdateUI());

    document.querySelectorAll('.bucketFilter').forEach(cb => {
        cb.addEventListener('change', () => { showLoading(); filterPricesAndUpdateUI(); });
    });

    document.querySelectorAll('.adv-filter-include, .adv-filter-exclude').forEach(cb => {
        cb.addEventListener('change', function () {
            const val = this.value;
            const isInc = this.classList.contains('adv-filter-include');
            if (isInc) {
                if (this.checked) {
                    const exc = document.getElementById(`exclude_${val}`);
                    if (exc) { exc.checked = false; selectedAdvancedExcludes.delete(val); }
                    selectedAdvancedIncludes.add(val);
                } else selectedAdvancedIncludes.delete(val);
            } else {
                if (this.checked) {
                    const inc = document.getElementById(`include_${val}`);
                    if (inc) { inc.checked = false; selectedAdvancedIncludes.delete(val); }
                    selectedAdvancedExcludes.add(val);
                } else selectedAdvancedExcludes.delete(val);
            }
            showLoading();
            filterPricesAndUpdateUI();
        });
    });

    document.getElementById('exportToExcelButton').addEventListener('click', async function () {
        if (currentlyFilteredPrices.length === 0) {
            showGlobalNotification('<p>Brak danych do eksportu.</p>');
            return;
        }

        showLoading();

        try {
            const wb = new ExcelJS.Workbook();
            const ws = wb.addWorksheet('Monitoring producenta');

            ws.columns = [
                { header: 'EAN', key: 'ean', width: 16 },
                { header: 'SKU', key: 'sku', width: 16 },
                { header: 'ID', key: 'externalId', width: 14 },
                { header: 'Producent', key: 'producer', width: 20 },
                { header: 'Nazwa produktu', key: 'name', width: 40 },
                { header: 'Wariant koloru', key: 'color', width: 16 },
                { header: 'Cena referencyjna', key: 'ref', width: 14, style: { numFmt: '0.00' } },
                { header: 'Źródło ref.', key: 'refSource', width: 16 },
                { header: 'MAP w katalogu', key: 'map', width: 14, style: { numFmt: '0.00' } },
                { header: 'Moja oferta', key: 'myPrice', width: 14, style: { numFmt: '0.00' } },
                { header: 'Najtańsza konkurencja', key: 'best', width: 14, style: { numFmt: '0.00' } },
                { header: 'Sklep konkurenta', key: 'bestStore', width: 20 },
                { header: 'Źródło konkurenta', key: 'bestSource', width: 14 },
                { header: 'Pozycja konkurenta', key: 'bestPos', width: 14 },
                { header: 'Konkurent dostępny', key: 'bestStock', width: 14 },
                { header: 'Delta (PLN)', key: 'deltaA', width: 12, style: { numFmt: '0.00' } },
                { header: 'Delta (%)', key: 'deltaP', width: 10, style: { numFmt: '0.00' } },
                { header: 'Status', key: 'bucket', width: 22 },
                { header: 'Sklepów łamiących', key: 'below', width: 14 },
                { header: 'Sklepów zgodnych', key: 'eq', width: 14 },
                { header: 'Sklepów powyżej', key: 'above', width: 14 },
                { header: 'Liczba ofert łącznie', key: 'storeCount', width: 14 },
                { header: 'Naruszenie obecne', key: 'isViolating', width: 14 },
                { header: 'Nowe naruszenie', key: 'isFresh', width: 14 },
                { header: 'Czas naruszenia (h)', key: 'violationHours', width: 18, style: { numFmt: '0.00' } },
                { header: 'Czas naruszenia (dni)', key: 'violationDays', width: 18, style: { numFmt: '0.00' } },
                { header: 'Pełne okno (7 dni)', key: 'reachedMax', width: 16 },
                { header: 'Niedawno zakończone', key: 'wasRecent', width: 18 },
                { header: 'Zakończone temu (h)', key: 'endedAgoHours', width: 18, style: { numFmt: '0.00' } },
                { header: 'Trwało (h)', key: 'lastDurHours', width: 14, style: { numFmt: '0.00' } },
                { header: 'Sprzedaż Ceneo (90d)', key: 'sales', width: 16 },
                { header: 'Trend sprzedaży', key: 'trendStatus', width: 14 },
                { header: 'Trend - różnica', key: 'salesDiff', width: 14 },
                { header: 'Trend - %', key: 'salesPct', width: 12, style: { numFmt: '0.00' } }
            ];

            ws.getRow(1).font = { bold: true };
            ws.getRow(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFE0E0E0' } };

            currentlyFilteredPrices.forEach(item => {
                const hours = item.violationDurationHours;
                const violationDays = hours != null ? Math.round((hours / 24) * 100) / 100 : null;
                const stockStr = item.bestCompetitorInStock === true ? 'TAK'
                    : (item.bestCompetitorInStock === false ? 'NIE' : 'Brak danych');
                const bestSourceStr = item.bestCompetitorIsGoogle === true ? 'Google'
                    : (item.bestCompetitorIsGoogle === false ? 'Ceneo' : '');

                ws.addRow({
                    ean: item.ean || '',
                    sku: item.producerCode || '',
                    externalId: item.externalId || '',
                    producer: item.producer || '',
                    name: item.productName || '',
                    color: item.googleColor || '',
                    ref: item.referencePrice,
                    refSource: item.referenceSource === 'map' ? 'MAP' : (item.referenceSource === 'store' ? 'Sklep' : 'Brak'),
                    map: item.mapPrice,
                    myPrice: item.myPrice,
                    best: item.bestCompetitorPrice,
                    bestStore: item.bestCompetitorStoreName || '',
                    bestSource: bestSourceStr,
                    bestPos: item.bestCompetitorPosition,
                    bestStock: stockStr,
                    deltaA: item.deltaAbsolute,
                    deltaP: item.deltaPercent,
                    bucket: BUCKET_LABELS[item.bucket] || '',
                    below: item.storesBelowReference,
                    eq: item.storesAtReference,
                    above: item.storesAboveReference,
                    storeCount: item.storeCount,
                    isViolating: item.isCurrentlyViolating ? 'TAK' : 'NIE',
                    isFresh: item.isFreshViolation ? 'TAK' : 'NIE',
                    violationHours: hours,
                    violationDays: violationDays,
                    reachedMax: item.reachedMaxWindow ? 'TAK' : 'NIE',
                    wasRecent: item.wasRecentlyViolated ? 'TAK' : 'NIE',
                    endedAgoHours: item.lastViolationEndedHoursAgo,
                    lastDurHours: item.lastViolationDurationHours,
                    sales: item.ceneoSalesCount,
                    trendStatus: item.salesTrendStatus || '',
                    salesDiff: item.salesDifference,
                    salesPct: item.salesPercentageChange
                });
            });

            const buffer = await wb.xlsx.writeBuffer();
            const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `Producent_${myStoreName}_${new Date().toISOString().slice(0, 10)}.xlsx`;
            a.click();
            URL.revokeObjectURL(url);

            showGlobalUpdate('<p style="font-weight:bold;">Eksport zakończony</p><p>Plik został pobrany.</p>');
        } catch (e) {
            console.error(e);
            showGlobalNotification('<p>Błąd eksportu.</p>');
        } finally {
            hideLoading();
        }
    });

    function generateSecureToken() {
        return 'xxxxxxxxxxxx4xxxyxxxxxxxxxxxxxxx'.replace(/[xy]/g, c => {
            const r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }
    function updateApiUrls(token) {
        if (!token) {
            $('#apiUrlJsonInput').val("Wygeneruj token, aby zobaczyć link.");
            $('#apiUrlXmlInput').val("Wygeneruj token, aby zobaczyć link.");
            return;
        }
        const baseUrl = window.location.origin;
        const base = `${baseUrl}/DataTree/export/${storeId}?token=${token}`;
        $('#apiUrlJsonInput').val(`${base}&format=json`);
        $('#apiUrlXmlInput').val(`${base}&format=xml`);
    }
    $('#apiExportModal').on('show.bs.modal', function () {
        $('#apiTokenInput').val('Ładowanie...');
        $.get(`/PriceHistory/GetApiExportSettings?storeId=${storeId}`, function (data) {
            $('#enableApiExportCheckbox').prop('checked', data.isApiExportEnabled);
            if (data.apiExportToken) { $('#apiTokenInput').val(data.apiExportToken); updateApiUrls(data.apiExportToken); }
            else { $('#apiTokenInput').val(''); updateApiUrls(''); }
        });
    });
    $('#generateTokenBtn').click(function () {
        const t = generateSecureToken();
        $('#apiTokenInput').val(t);
        updateApiUrls(t);
    });
    $('#saveApiExportBtn').click(function () {
        const e = $('#enableApiExportCheckbox').is(':checked');
        const t = $('#apiTokenInput').val();
        if (e && (!t || t.trim() === '' || t === 'Ładowanie...')) { alert('Jeśli włączasz API, musisz wygenerować token!'); return; }
        showLoading();
        $.ajax({
            url: `/PriceHistory/SaveApiExportSettings?storeId=${storeId}`,
            type: 'POST', contentType: 'application/json',
            data: JSON.stringify({ isEnabled: e, token: t }),
            success: function (resp) { hideLoading(); $('#apiExportModal').modal('hide'); showGlobalUpdate(resp.message); },
            error: function () { hideLoading(); showGlobalNotification('Błąd zapisu.'); }
        });
    });
    $(document).on('click', '#apiExportModal .copy-btn', function () {
        const tid = $(this).data('target');
        const inp = document.getElementById(tid);
        if (!inp.value || inp.value.includes("Wygeneruj token")) return;
        inp.select(); inp.setSelectionRange(0, 99999);
        navigator.clipboard.writeText(inp.value).then(() => {
            const $b = $(this);
            const orig = $b.html();
            $b.html('<i class="fa-solid fa-check"></i>');
            setTimeout(() => $b.html(orig), 2000);
        });
    });

    loadPrices();
    massActions.updateSelectionUI();
});