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
        sortTotalPopularity: null,
        sortMyPopularity: null,
        sortMarketShare: null
    };

    let offerSlider, myPriceSlider;

    const allegroProducerCatalogStorageKey = `allegroProducerCatalogViewState_${storeId}`;
    let isCatalogViewActive = false;
    let activeCatalogGroupFilter = null;
    let catalogGroupMap = new Map();

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
        isAllegro: true,
        storageKey: `selectedAllegroProducts_${storeId}`,
        flags: typeof flags !== 'undefined' ? flags : [],
        getAllPrices: () => allPrices,
        getFilteredPrices: () => currentlyFilteredPrices,
        getProductIdentifier: (product) => {
            switch (producerSettings.identifierForSimulation) {
                case 'ID': return { label: 'ID', value: product.idOnAllegro || null };
                case 'SKU': return { label: 'SKU', value: product.allegroSku || null };
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

    function getOfferText(count) {
        if (count === 1) return `${count} Oferta`;
        const lastDigit = count % 10;
        if (count > 10 && count < 20) return `${count} Ofert`;
        if ([2, 3, 4].includes(lastDigit)) return `${count} Oferty`;
        return `${count} Ofert`;
    }

    function renderDeliveryInfo(deliveryTime) {
        if (deliveryTime === null || deliveryTime === undefined) {
            return '<div class="Delivery3">Brak danych</div>';
        }
        let text = '';
        let className = '';
        if (deliveryTime === 0) { text = 'Wysyłka natychmiast'; className = 'Delivery1'; }
        else if (deliveryTime === 1) { text = 'Dostawa jutro'; className = 'Delivery1'; }
        else if (deliveryTime === 2) { text = 'Dostawa pojutrze'; className = 'Delivery2'; }
        else { text = `Dostawa za ${deliveryTime} dni`; className = 'Delivery3'; }
        return `<div class="${className}">${text}</div>`;
    }

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

        fetch(`/AllegroPriceHistory/GetAllegroPricesForProducer?storeId=${storeId}`)
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

                const compCounts = allPrices.map(i => i.competitorCount || 0);
                const maxCompCount = Math.max(1, ...compCounts);
                offerSlider.noUiSlider.updateOptions({ range: { 'min': 1, 'max': maxCompCount } });
                offerSlider.noUiSlider.set([1, maxCompCount]);

                const totalProductCountEl = document.getElementById('totalProductCount');
                if (totalProductCountEl) totalProductCountEl.textContent = response.productCount || allPrices.length;

                const totalPriceCountEl = document.getElementById('totalPriceCount');
                if (totalPriceCountEl) {
                    const totalOffers = allPrices.reduce((sum, p) => sum + (p.competitorCount || 0) + (p.myPrice != null ? 1 : 0), 0);
                    totalPriceCountEl.textContent = totalOffers;
                }

                buildCatalogGroupInfo();

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

    function buildCatalogGroupInfo() {
        catalogGroupMap.clear();
        const groups = [];

        for (const item of allPrices) {
            if (!item.myOffersGroupKey) continue;
            const itemIds = new Set(item.myOffersGroupKey.split(',').filter(Boolean));
            if (itemIds.size === 0) continue;

            let matchedIndices = [];
            for (let i = 0; i < groups.length; i++) {
                if ([...itemIds].some(id => groups[i].mergedIds.has(id))) {
                    matchedIndices.push(i);
                }
            }

            if (matchedIndices.length === 0) {
                groups.push({ mergedIds: new Set(itemIds), products: [item] });
            } else {
                const primary = groups[matchedIndices[0]];
                itemIds.forEach(id => primary.mergedIds.add(id));
                primary.products.push(item);

                for (let i = matchedIndices.length - 1; i >= 1; i--) {
                    const other = groups[matchedIndices[i]];
                    other.mergedIds.forEach(id => primary.mergedIds.add(id));
                    primary.products.push(...other.products);
                    groups.splice(matchedIndices[i], 1);
                }
            }
        }

        for (const group of groups) {
            if (group.products.length <= 1) continue;

            const withPrice = group.products.filter(p => p.myPrice != null && !p.isRejected);
            const leader = withPrice.length > 0
                ? withPrice.reduce((best, p) => parseFloat(p.myPrice) < parseFloat(best.myPrice) ? p : best)
                : null;

            const productIdSet = new Set(group.products.map(p => p.productId));

            for (const product of group.products) {
                catalogGroupMap.set(product.productId, {
                    groupProductIds: productIdSet,
                    totalInGroup: group.products.length,
                    isLeader: leader != null && product.productId === leader.productId
                });
            }
        }
    }

    function groupAndFilterByCatalog(data) {
        const catalogGroups = new Map();

        for (const item of data) {
            if (!item.myOffersGroupKey) {
                catalogGroups.set(`no-group-${item.productId}`, item);
                continue;
            }

            const itemIds = new Set(item.myOffersGroupKey.split(','));

            let matchedKey = null;
            for (const [existingKey] of catalogGroups) {
                if (existingKey.startsWith('no-group-')) continue;

                const existingIds = new Set(existingKey.split(','));
                const hasOverlap = [...itemIds].some(id => existingIds.has(id));

                if (hasOverlap) {
                    matchedKey = existingKey;
                    break;
                }
            }

            if (matchedKey) {
                const existingItem = catalogGroups.get(matchedKey);
                const existingIds = new Set(matchedKey.split(','));
                const mergedKey = [...new Set([...existingIds, ...itemIds])].sort().join(',');

                const betterItem = (item.myPrice !== null
                    && (existingItem.myPrice === null || parseFloat(item.myPrice) < parseFloat(existingItem.myPrice)))
                    ? item
                    : existingItem;

                catalogGroups.delete(matchedKey);
                catalogGroups.set(mergedKey, betterItem);
            } else {
                const canonicalKey = [...itemIds].sort().join(',');
                catalogGroups.set(canonicalKey, item);
            }
        }

        return Array.from(catalogGroups.values());
    }

    function showCatalogGroup(productId) {
        const info = catalogGroupMap.get(productId);
        if (!info) return;

        if (isCatalogViewActive) {
            isCatalogViewActive = false;
            localStorage.setItem(allegroProducerCatalogStorageKey, JSON.stringify(false));
        }

        activeCatalogGroupFilter = info.groupProductIds;
        currentPage = 1;
        updateCatalogButton();
        filterPricesAndUpdateUI();
    }

    function clearCatalogGroupFilter() {
        activeCatalogGroupFilter = null;
        updateCatalogButton();
        filterPricesAndUpdateUI();
    }

    function updateCatalogButton() {
        const btn = document.getElementById('linkOffers');
        if (!btn) return;

        if (activeCatalogGroupFilter) {
            btn.classList.remove('active');
            btn.classList.add('catalog-group-active');
            btn.innerHTML = 'Wybrany Katalog <i class="fa-regular fa-rectangle-xmark" style="margin-left:8px; font-size:13px;"></i>';
        } else {
            btn.classList.remove('catalog-group-active');
            btn.textContent = 'Katalog';
            btn.classList.toggle('active', isCatalogViewActive);
        }
    }

    function updateCatalogFilterCounts(prices) {
        let multiCount = 0;
        let leaderCount = 0;
        prices.forEach(item => {
            const info = catalogGroupMap.get(item.productId);
            if (info) {
                multiCount++;
                if (info.isLeader) leaderCount++;
            }
        });
        const multiEl = document.getElementById('label_catalogMulti');
        const leaderEl = document.getElementById('label_catalogLeader');
        if (multiEl) multiEl.textContent = `Wielokrotne wys. (${multiCount})`;
        if (leaderEl) leaderEl.textContent = `Główna oferta (${leaderCount})`;
    }

    function restoreCatalogState() {
        const stored = localStorage.getItem(allegroProducerCatalogStorageKey);
        if (stored !== null) {
            try {
                isCatalogViewActive = JSON.parse(stored);
                const btn = document.getElementById('linkOffers');
                if (btn && isCatalogViewActive) btn.classList.add('active');
            } catch (e) {
                localStorage.removeItem(allegroProducerCatalogStorageKey);
            }
        }
    }

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
            AllegroIdentifierForSimulation: producerSettings.identifierForSimulation,
            AllegroProducerComparisonSource: producerSettings.comparisonSource,
            AllegroProducerUseAmount: currentMode === 'amt',
            AllegroProducerThresholdRedDarkPercent: thresholdsState.pct.redDark,
            AllegroProducerThresholdRedPercent: thresholdsState.pct.red,
            AllegroProducerThresholdRedLightPercent: thresholdsState.pct.redLight,
            AllegroProducerThresholdGreenLightPercent: thresholdsState.pct.greenLight,
            AllegroProducerThresholdGreenPercent: thresholdsState.pct.green,
            AllegroProducerThresholdGreenDarkPercent: thresholdsState.pct.greenDark,
            AllegroProducerThresholdRedDarkAmount: thresholdsState.amt.redDark,
            AllegroProducerThresholdRedAmount: thresholdsState.amt.red,
            AllegroProducerThresholdRedLightAmount: thresholdsState.amt.redLight,
            AllegroProducerThresholdGreenLightAmount: thresholdsState.amt.greenLight,
            AllegroProducerThresholdGreenAmount: thresholdsState.amt.green,
            AllegroProducerThresholdGreenDarkAmount: thresholdsState.amt.greenDark
        };

        showLoading();
        fetch('/AllegroPriceHistory/SaveAllegroProducerSettings', {
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

    function updateBadgeCounts(prices) {
        let counts = { ss: 0, sp: 0, top: 0, bpg: 0, smart: 0, promo: 0 };
        prices.forEach(p => {
            if (p.bestCompetitorSuperSeller) counts.ss++;
            if (p.bestCompetitorSuperPrice) counts.sp++;
            if (p.bestCompetitorTopOffer) counts.top++;
            if (p.bestCompetitorIsBestPriceGuarantee) counts.bpg++;
            if (p.bestCompetitorSmart) counts.smart++;
            if (p.bestCompetitorPromoted || p.bestCompetitorSponsored) counts.promo++;
        });

        const setLabel = (id, text, count) => {
            const el = document.getElementById(id);
            if (el) el.textContent = `${text} (${count})`;
        };
        setLabel('label_compIsSuperSeller', 'Super Sprzedawca', counts.ss);
        setLabel('label_compIsSuperPrice', 'Super cena', counts.sp);
        setLabel('label_compIsTopOffer', 'Top oferta', counts.top);
        setLabel('label_compIsBestPriceGuarantee', 'Gwar. naj. ceny', counts.bpg);
        setLabel('label_compIsSmart', 'Smart!', counts.smart);
        setLabel('label_compIsPromoted', 'Promowane/Sponsor.', counts.promo);
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

            case 'catalogMulti':
                return catalogGroupMap.has(item.productId);
            case 'catalogLeader':
                return catalogGroupMap.get(item.productId)?.isLeader === true;

            case 'compIsSuperSeller': return item.bestCompetitorSuperSeller === true;
            case 'compIsSuperPrice': return item.bestCompetitorSuperPrice === true;
            case 'compIsTopOffer': return item.bestCompetitorTopOffer === true;
            case 'compIsBestPriceGuarantee': return item.bestCompetitorIsBestPriceGuarantee === true;
            case 'compIsSmart': return item.bestCompetitorSmart === true;
            case 'compIsPromoted': return item.bestCompetitorPromoted === true || item.bestCompetitorSponsored === true;

            case 'deliveryFast':
                return item.bestCompetitorDeliveryTime != null && item.bestCompetitorDeliveryTime <= 1;
            case 'deliverySlow':
                return item.bestCompetitorDeliveryTime != null && item.bestCompetitorDeliveryTime >= 3;
            case 'deliveryNoData':
                return item.bestCompetitorDeliveryTime == null;

            default: return false;
        }
    }

    function filterPricesByCategoryAndColorAndFlag(data) {
        let filtered = data;

        const selBuckets = Array.from(document.querySelectorAll('.bucketFilter:checked')).map(c => c.value);
        if (selBuckets.length) filtered = filtered.filter(item => selBuckets.includes(item.bucket));

        const offVals = offerSlider.noUiSlider.get();
        const offMin = parseInt(offVals[0]), offMax = parseInt(offVals[1]);
        const priceRaw = myPriceSlider.noUiSlider.get();
        const priceMin = parseFloat(priceRaw[0].replace(' PLN', '').replace(/\s/g, '').replace(',', '.'));
        const priceMax = parseFloat(priceRaw[1].replace(' PLN', '').replace(/\s/g, '').replace(',', '.'));

        filtered = filtered.filter(item => {
            const count = item.competitorCount || 0;
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

            if (isCatalogViewActive) {
                filtered = groupAndFilterByCatalog(filtered);
            }

            if (activeCatalogGroupFilter) {
                filtered = filtered.filter(item => activeCatalogGroupFilter.has(item.productId));
            }

            const productSearch = document.getElementById('productSearch').value.trim();
            const storeSearch = document.getElementById('storeSearch').value.trim();

            if (productSearch) {
                const sanitized = productSearch.replace(/[^a-zA-Z0-9\s.-]/g, '').toLowerCase().replace(/\s+/g, '');
                filtered = filtered.filter(p => {
                    let id = '';
                    switch (producerSettings.identifierForSimulation) {
                        case 'ID': id = p.idOnAllegro || ''; break;
                        case 'SKU': id = p.allegroSku || ''; break;
                        default: id = p.ean || ''; break;
                    }
                    const combo = ((p.productName || '') + ' ' + id).toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
                    return combo.includes(sanitized);
                });
            }

            if (storeSearch) {
                const sanitized = storeSearch.replace(/[^a-zA-Z0-9\s.-]/g, '').toLowerCase().replace(/\s+/g, '');
                filtered = filtered.filter(p => {
                    const sn = (p.bestCompetitorSellerName || '').toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
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
            } else if (sortingState.sortTotalPopularity !== null) {
                filtered.sort((a, b) => sortingState.sortTotalPopularity === 'asc' ? (b.totalPopularity ?? -Infinity) - (a.totalPopularity ?? -Infinity) : (a.totalPopularity ?? Infinity) - (b.totalPopularity ?? Infinity));
            } else if (sortingState.sortMyPopularity !== null) {
                filtered.sort((a, b) => sortingState.sortMyPopularity === 'asc' ? (b.myTotalPopularity ?? -Infinity) - (a.myTotalPopularity ?? -Infinity) : (a.myTotalPopularity ?? Infinity) - (b.myTotalPopularity ?? Infinity));
            } else if (sortingState.sortMarketShare !== null) {
                filtered.sort((a, b) => sortingState.sortMarketShare === 'asc' ? (b.marketSharePercentage ?? -Infinity) - (a.marketSharePercentage ?? -Infinity) : (a.marketSharePercentage ?? Infinity) - (b.marketSharePercentage ?? Infinity));
            }

            const selProducer = document.getElementById('producerFilterDropdown').value;
            if (selProducer) filtered = filtered.filter(i => i.producer === selProducer);

            currentlyFilteredPrices = [...filtered];

            renderPrices(filtered);
            renderChart(filtered);
            updateBucketCountsUI(filtered);
            updateFlagCounts(filtered);
            updateBadgeCounts(filtered);
            updateViolationCounts(filtered);
            updateNewProductCount(filtered);
            updateCatalogFilterCounts(filtered);
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
        let commissionText = "Brak danych";
        if (item.apiAllegroCommission != null) {
            commissionText = formatPricePL(item.apiAllegroCommission, false) + " PLN";
        }

        const blocks = [
            `<div class="price-box-column-offers-a">
                <span class="data-channel"><img src="/images/AllegroIcon.png" alt="Allegro" style="width:15px;height:15px;" /></span>
                <div class="offer-count-box">${getOfferText(item.competitorCount || 0)}</div>
            </div>`,

            `<div class="price-box-column-offers-a" title="Łączna sprzedaż w katalogu (30 dni)">
                <span class="data-channel"><i class="fas fa-shopping-cart" style="font-size: 15px; color: grey; margin-top:1px;"></i></span>
                <div class="offer-count-box">${item.totalPopularity || 0} osb. kupiło</div>
            </div>`,

            `<div class="price-box-column-offers-a" title="Sprzedaż Twojej oferty / udział w rynku">
                <span class="data-channel"><i class="fas fa-chart-pie" style="font-size: 15px; color: grey; margin-top:1px;"></i></span>
                <div class="offer-count-box">${item.myTotalPopularity || 0} osb. (${(item.marketSharePercentage || 0).toFixed(2)}%)</div>
            </div>`,

            `<div class="price-box-column-offers-a" title="Prowizja Allegro dla Twojej oferty">
                <span class="data-channel"><i class="fa-solid fa-coins" style="font-size:14px; color:#888;"></i></span>
                <div class="offer-count-box">${commissionText}</div>
            </div>`
        ];

      
        return blocks.map(block => block.trim()).join('');
    }

    function buildCompetitorBox(item, storeSearchTerm) {
        const bestComp = item.bestCompetitorPrice != null ? parseFloat(item.bestCompetitorPrice) : null;
        const highlightedStoreName = highlightMatches(item.bestCompetitorSellerName || '', storeSearchTerm);

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

        const superPriceBadge = item.bestCompetitorSuperPrice ? `<div class="SuperPrice">SUPERCENA</div>` : '';
        const topOfferBadge = item.bestCompetitorTopOffer ? `<div class="TopOffer">Top oferta</div>` : '';
        const bestPriceStyle = item.bestCompetitorIsBestPriceGuarantee ? 'color: #169A23;' : '';
        const bestPriceIcon = item.bestCompetitorIsBestPriceGuarantee
            ? `<img src="/images/TopPrice.png" alt="Gwarancja Najniższej Ceny" title="Gwarancja Najniższej Ceny" style="width: 18px; height: 18px; vertical-align: middle;">`
            : '';
        const superSellerIcon = item.bestCompetitorSuperSeller
            ? `<img src="/images/SuperSeller.png" alt="Super Sprzedawca" title="Super Sprzedawca" style="width: 16px; height: 16px; vertical-align: middle;">`
            : '';
        const smartBadge = item.bestCompetitorSmart
            ? `<div class="Smart-Allegro"><img src="/images/Smart.png" alt="Smart!" title="Smart!" style="height: 15px; width: auto; margin-left: 2px;"></div>`
            : '';
        let promoText = '';
        if (item.bestCompetitorPromoted) promoText = ` <span class="AddPromoInfoBadge">Promowane</span>`;
        else if (item.bestCompetitorSponsored) promoText = ` <span class="AddPromoInfoBadge">Sponsorowane</span>`;

        return `
            <div class="price-box-column">
                <div class="price-box-column-text">
                    <div style="display:flex; align-items:center; gap:4px; flex-wrap:wrap;">
                        <span style="font-weight:500; font-size:17px; ${bestPriceStyle}">${formatPricePL(bestComp)}</span>
                        ${bestPriceIcon}${superPriceBadge}${topOfferBadge}
                    </div>
                    <div style="color:#444; font-size:13px;">
                        <span>${highlightedStoreName}</span>${superSellerIcon}${promoText}
                    </div>
                </div>
                <div class="price-box-column-text">
                    <div class="data-channel">
                        ${smartBadge}
                        ${renderDeliveryInfo(item.bestCompetitorDeliveryTime)}
                    </div>
                </div>
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

            let stateBadgeClass, stateBadgeText, iconClass, iconColor, barStateClass, headerText, durationText, metaHtml;

            if (isFresh) {
                stateBadgeClass = 'violation-state-fresh';
                stateBadgeText = 'Nowe';
                iconClass = 'fa-solid fa-bell';
                iconColor = '#9D4EDD';
                barStateClass = 'fresh';
                headerText = 'Nowe naruszenie';
                durationText = 'Wykryto ostatnio';
                metaHtml = '<div class="violation-detail-meta">Pierwsze wykrycie.</div>';
            } else {
                stateBadgeClass = 'violation-state-active';
                stateBadgeText = 'Aktywne';
                iconClass = 'fa-solid fa-triangle-exclamation';
                iconColor = '#8b1a1a';
                barStateClass = 'active';
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
                if (barPercent >= 75) {
                    barFillColor = '#c0392b';
                } else if (barPercent >= 35) {
                    barFillColor = '#f39c12';
                } else {
                    barFillColor = '#f4d03f';
                }
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
                                <div class="violation-time-bar-fill" style="width:${barPercent.toFixed(1)}%; background-color: ${barFillColor};"></div>
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


    function buildReferenceBox(item) {
        const refPrice = item.referencePrice != null ? parseFloat(item.referencePrice) : null;
        const myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
        const mapPrice = item.mapPrice != null ? parseFloat(item.mapPrice) : null;

        const myBpgIcon = item.myIsBestPriceGuarantee
            ? `<img src="/images/TopPrice.png" alt="Gwarancja Najniższej Ceny" title="Gwarancja Najniższej Ceny" style="width: 18px; height: 18px; vertical-align: middle;">`
            : '';
        const mySuperSellerIcon = item.myIsSuperSeller
            ? `<img src="/images/SuperSeller.png" alt="Super Sprzedawca" title="Super Sprzedawca" style="width: 16px; height: 16px; vertical-align: middle;">`
            : '';
        const mySmartBadge = item.myIsSmart
            ? `<div class="Smart-Allegro"><img src="/images/Smart.png" alt="Smart!" title="Smart!" style="height: 15px; width: auto; margin-left: 2px;"></div>`
            : '';
        const mySmartInline = item.myIsSmart
            ? `<img src="/images/Smart.png" alt="Smart!" title="Smart!" style="height: 13px; width: auto; vertical-align: middle; margin-left: 4px;">`
            : '';
        const mySuperPriceBadge = item.myIsSuperPrice ? `<div class="SuperPrice">SUPERCENA</div>` : '';
        const myTopOfferBadge = item.myIsTopOffer ? `<div class="TopOffer">Top oferta</div>` : '';

        if (refPrice != null && refPrice > 0) {
            const sourceLabel = item.referenceSource === 'map' ? 'Cena MAP' : 'Cena Twojego sklepu na Allegro';

            const altLines = [];
            if (item.referenceSource === 'map' && myPrice != null && myPrice > 0) {
                altLines.push(`<div class="ref-alt-line">Twoja oferta na Allegro: <strong>${formatPricePL(myPrice)}</strong>${mySmartInline}</div>`);
            }
            if (item.referenceSource === 'store' && mapPrice != null && mapPrice > 0) {
                altLines.push(`<div class="ref-alt-line">MAP w katalogu: <strong>${formatPricePL(mapPrice)}</strong></div>`);
            }

            const showOfferDetailsRow = item.referenceSource === 'store' && myPrice != null;

            return `
                <div class="price-box-column">
                    <div class="price-box-column-text">
                        <div><span class="ref-label-small">${sourceLabel}</span></div>
                        <div style="display:flex; align-items:center; gap:4px; flex-wrap:wrap;">
                            <span style="font-weight:500; font-size:17px;">${formatPricePL(refPrice)}</span>
                            ${item.referenceSource === 'store' ? `${myBpgIcon}${mySuperPriceBadge}${myTopOfferBadge}` : ''}
                        </div>
                        ${item.referenceSource === 'store' ? `<div style="color:#444; font-size:13px;"><span>${myStoreName || ''}</span>${mySuperSellerIcon}</div>` : ''}
                        ${altLines.join('')}
                    </div>
                    <div class="price-box-column-text">
                        ${showOfferDetailsRow
                    ? `<div class="data-channel">${mySmartBadge}${renderDeliveryInfo(item.myDeliveryTime)}</div>`
                    : ''}
                    </div>
                </div>`;
        }

        let missingMsg = '';
        if (producerSettings.comparisonSource === 1) missingMsg = 'Brak ceny MAP w katalogu';
        else missingMsg = 'Brak Twojej oferty na Allegro';

        const altLines = [];
        if (mapPrice != null && mapPrice > 0) altLines.push(`<div class="ref-alt-line">MAP w katalogu: <strong>${formatPricePL(mapPrice)}</strong></div>`);
        if (myPrice != null && myPrice > 0) altLines.push(`<div class="ref-alt-line">Twoja oferta na Allegro: <strong>${formatPricePL(myPrice)}</strong>${mySmartInline}</div>`);

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
        } else {
            rowClass = 'neutral';
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
            box.dataset.detailsUrl = `/AllegroPriceHistory/Details?storeId=${storeId}&productId=${item.productId}&scrapId=${currentScrapId}`;
            box.dataset.productId = item.productId;
            box.dataset.productName = item.productName;
            box.addEventListener('click', function (event) {
                if (event.target.closest('button, a, img, .select-product-btn, .ApiBox, .badge-catalog')) return;
                window.open(this.dataset.detailsUrl, '_blank');
            });

            const priceBoxSpace = document.createElement('div');
            priceBoxSpace.className = 'price-box-space';

            const leftCol = document.createElement('div');
            leftCol.className = 'price-box-left-column';

            const nameDiv = document.createElement('div');
            nameDiv.className = 'price-box-column-name';
            nameDiv.innerHTML = highlightedName;

            const catalogInfo = catalogGroupMap.get(item.productId);
            if (item.isNew || catalogInfo) {
                const badgesHtml = [];

                if (catalogInfo) {
                    const count = catalogInfo.totalInGroup;
                    const offerWord = count === 1 ? 'Oferta' : ((count >= 2 && count <= 4) ? 'Oferty' : 'Ofert');
                    const isLeader = catalogInfo.isLeader;
                    const leaderSuffix = isLeader
                        ? ' | <i class="fa-solid fa-crown" style="color:#e6a817; margin:2px 0px 0px 4px;"></i>'
                        : '';

                    badgesHtml.push(
                        `<span class="badge-catalog catalog-badge-click" data-product-id="${item.productId}" title="Pokaż wszystkie ${count} oferty w tym katalogu">
                            ${count} ${offerWord} w katalogu${leaderSuffix}
                        </span>`
                    );
                }

                if (item.isNew) {
                    badgesHtml.push('<span class="badge-new">NEW</span>');
                }

                nameDiv.insertAdjacentHTML('beforeend',
                    `<div style="display:flex; align-items:center; gap:5px; margin-top:3px; flex-wrap:wrap;">
                        ${badgesHtml.join('')}
                    </div>`
                );

                const catalogBadgeEl = nameDiv.querySelector('.catalog-badge-click');
                if (catalogBadgeEl) {
                    catalogBadgeEl.addEventListener('click', function (e) {
                        e.stopPropagation();
                        showCatalogGroup(item.productId);
                    });
                }
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
                case 'ID': idVal = item.idOnAllegro || null; idLabel = 'ID'; break;
                case 'SKU': idVal = item.allegroSku || null; idLabel = 'SKU'; break;
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
            case 'sortTotalPopularity': return 'Sprzedaż katalogu';
            case 'sortMyPopularity': return 'Moja Sprzedaż';
            case 'sortMarketShare': return 'Udział %';
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
            localStorage.setItem('allegroProducerSorting_' + storeId, JSON.stringify(sortingState));
            filterPricesAndUpdateUI();
        });
    }

    ['sortName', 'sortPrice', 'sortDeltaPercent', 'sortDeltaAmount',
        'sortViolationDuration', 'sortStoresViolating',
        'sortTotalPopularity', 'sortMyPopularity', 'sortMarketShare']
        .forEach(bindSortButton);

    const storedSort = localStorage.getItem('allegroProducerSorting_' + storeId);
    if (storedSort) {
        try {
            const parsed = JSON.parse(storedSort);
            const validKeys = Object.keys(sortingState);
            const cleaned = {};
            validKeys.forEach(k => { if (parsed[k] !== undefined) cleaned[k] = parsed[k]; });
            sortingState = { ...sortingState, ...cleaned };
            updateSortButtonVisuals();
        } catch (e) {
            localStorage.removeItem('allegroProducerSorting_' + storeId);
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

    const linkOffersBtn = document.getElementById('linkOffers');
    if (linkOffersBtn) {
        linkOffersBtn.addEventListener('click', function () {
            if (activeCatalogGroupFilter) {
                clearCatalogGroupFilter();
                return;
            }
            isCatalogViewActive = !isCatalogViewActive;
            this.classList.toggle('active', isCatalogViewActive);
            localStorage.setItem(allegroProducerCatalogStorageKey, JSON.stringify(isCatalogViewActive));
            filterPricesAndUpdateUI();
        });
    }

    document.getElementById('exportToExcelButton').addEventListener('click', async function () {
        if (currentlyFilteredPrices.length === 0) {
            showGlobalNotification('<p>Brak danych do eksportu.</p>');
            return;
        }

        showLoading();

        try {
            const wb = new ExcelJS.Workbook();
            const ws = wb.addWorksheet('Monitoring Allegro');

            ws.columns = [
                { header: 'EAN', key: 'ean', width: 16 },
                { header: 'SKU', key: 'sku', width: 16 },
                { header: 'ID Allegro', key: 'idAllegro', width: 16 },
                { header: 'Producent', key: 'producer', width: 20 },
                { header: 'Nazwa produktu', key: 'name', width: 40 },
                { header: 'Link do oferty', key: 'url', width: 30 },
                { header: 'Liczba moich ofert w katalogu', key: 'myOfferCount', width: 18 },
                { header: 'Cena referencyjna', key: 'ref', width: 14, style: { numFmt: '0.00' } },
                { header: 'Źródło ref.', key: 'refSource', width: 16 },
                { header: 'MAP w katalogu', key: 'map', width: 14, style: { numFmt: '0.00' } },
                { header: 'Moja oferta', key: 'myPrice', width: 14, style: { numFmt: '0.00' } },
                { header: 'Najtańsza konkurencja', key: 'best', width: 14, style: { numFmt: '0.00' } },
                { header: 'Sklep konkurenta', key: 'bestStore', width: 20 },
                { header: 'Dostawa (dni)', key: 'bestDelivery', width: 14 },
                { header: 'Super Sprzedawca', key: 'bestSS', width: 12 },
                { header: 'Super Cena', key: 'bestSP', width: 12 },
                { header: 'Top Oferta', key: 'bestTop', width: 12 },
                { header: 'Gwar. Naj. Ceny', key: 'bestBPG', width: 14 },
                { header: 'Smart konkurenta', key: 'bestSmart', width: 14 },
                { header: 'Promowane/Sponsor.', key: 'bestPromo', width: 16 },
                { header: 'Delta (PLN)', key: 'deltaA', width: 12, style: { numFmt: '0.00' } },
                { header: 'Delta (%)', key: 'deltaP', width: 10, style: { numFmt: '0.00' } },
                { header: 'Status', key: 'bucket', width: 22 },
                { header: 'Sklepów łamiących', key: 'below', width: 14 },
                { header: 'Sklepów zgodnych', key: 'eq', width: 14 },
                { header: 'Sklepów powyżej', key: 'above', width: 14 },
                { header: 'Liczba konkurentów', key: 'compCount', width: 14 },
                { header: 'Naruszenie obecne', key: 'isViolating', width: 14 },
                { header: 'Nowe naruszenie', key: 'isFresh', width: 14 },
                { header: 'Czas naruszenia (h)', key: 'violationHours', width: 18, style: { numFmt: '0.00' } },
                { header: 'Czas naruszenia (dni)', key: 'violationDays', width: 18, style: { numFmt: '0.00' } },
                { header: 'Pełne okno (7 dni)', key: 'reachedMax', width: 16 },
                { header: 'Niedawno zakończone', key: 'wasRecent', width: 18 },
                { header: 'Zakończone temu (h)', key: 'endedAgoHours', width: 18, style: { numFmt: '0.00' } },
                { header: 'Trwało (h)', key: 'lastDurHours', width: 14, style: { numFmt: '0.00' } },
                { header: 'Sprzedaż katalogu (30d)', key: 'totalPop', width: 18 },
                { header: 'Moja sprzedaż (30d)', key: 'myPop', width: 18 },
                { header: 'Udział rynkowy (%)', key: 'marketShare', width: 16, style: { numFmt: '0.00' } },
                { header: 'Prowizja Allegro', key: 'commission', width: 14, style: { numFmt: '0.00' } }
            ];

            ws.getRow(1).font = { bold: true };
            ws.getRow(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFE0E0E0' } };

            currentlyFilteredPrices.forEach(item => {
                let promoStr = '';
                if (item.bestCompetitorPromoted) promoStr = 'Promowane';
                else if (item.bestCompetitorSponsored) promoStr = 'Sponsorowane';

                const hours = item.violationDurationHours;
                const violationDays = hours != null ? Math.round((hours / 24) * 100) / 100 : null;

                ws.addRow({
                    ean: item.ean || '',
                    sku: item.allegroSku || '',
                    idAllegro: item.idOnAllegro || '',
                    producer: item.producer || '',
                    name: item.productName || '',
                    url: item.allegroOfferUrl || '',
                    myOfferCount: item.myOfferCount || 1,
                    ref: item.referencePrice,
                    refSource: item.referenceSource === 'map' ? 'MAP' : (item.referenceSource === 'store' ? 'Sklep' : 'Brak'),
                    map: item.mapPrice,
                    myPrice: item.myPrice,
                    best: item.bestCompetitorPrice,
                    bestStore: item.bestCompetitorSellerName || '',
                    bestDelivery: item.bestCompetitorDeliveryTime,
                    bestSS: item.bestCompetitorSuperSeller ? 'TAK' : 'NIE',
                    bestSP: item.bestCompetitorSuperPrice ? 'TAK' : 'NIE',
                    bestTop: item.bestCompetitorTopOffer ? 'TAK' : 'NIE',
                    bestBPG: item.bestCompetitorIsBestPriceGuarantee ? 'TAK' : 'NIE',
                    bestSmart: item.bestCompetitorSmart ? 'TAK' : 'NIE',
                    bestPromo: promoStr,
                    deltaA: item.deltaAbsolute,
                    deltaP: item.deltaPercent,
                    bucket: BUCKET_LABELS[item.bucket] || '',
                    below: item.storesBelowReference,
                    eq: item.storesAtReference,
                    above: item.storesAboveReference,
                    compCount: item.competitorCount,
                    isViolating: item.isCurrentlyViolating ? 'TAK' : 'NIE',
                    isFresh: item.isFreshViolation ? 'TAK' : 'NIE',
                    violationHours: hours,
                    violationDays: violationDays,
                    reachedMax: item.reachedMaxWindow ? 'TAK' : 'NIE',
                    wasRecent: item.wasRecentlyViolated ? 'TAK' : 'NIE',
                    endedAgoHours: item.lastViolationEndedHoursAgo,
                    lastDurHours: item.lastViolationDurationHours,
                    totalPop: item.totalPopularity,
                    myPop: item.myTotalPopularity,
                    marketShare: item.marketSharePercentage,
                    commission: item.apiAllegroCommission
                });
            });

            const buffer = await wb.xlsx.writeBuffer();
            const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `Producent_Allegro_${myStoreName}_${new Date().toISOString().slice(0, 10)}.xlsx`;
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
            $('#allegroApiUrlJsonInput').val("Wygeneruj token, aby zobaczyć link.");
            $('#allegroApiUrlXmlInput').val("Wygeneruj token, aby zobaczyć link.");
            return;
        }
        const baseUrl = window.location.origin;
        const base = `${baseUrl}/DataTree/export-allegro/${storeId}?token=${token}`;
        $('#allegroApiUrlJsonInput').val(`${base}&format=json`);
        $('#allegroApiUrlXmlInput').val(`${base}&format=xml`);
    }
    $('#allegroApiExportModal').on('show.bs.modal', function () {
        $('#allegroApiTokenInput').val('Ładowanie...');
        $.get(`/AllegroPriceHistory/GetAllegroApiExportSettings?storeId=${storeId}`, function (data) {
            $('#enableAllegroApiExportCheckbox').prop('checked', data.isApiExportEnabled);
            if (data.apiExportToken) { $('#allegroApiTokenInput').val(data.apiExportToken); updateApiUrls(data.apiExportToken); }
            else { $('#allegroApiTokenInput').val(''); updateApiUrls(''); }
        });
    });
    $('#generateAllegroTokenBtn').click(function () {
        const t = generateSecureToken();
        $('#allegroApiTokenInput').val(t);
        updateApiUrls(t);
    });
    $('#saveAllegroApiExportBtn').click(function () {
        const e = $('#enableAllegroApiExportCheckbox').is(':checked');
        const t = $('#allegroApiTokenInput').val();
        if (e && (!t || t.trim() === '' || t === 'Ładowanie...')) { alert('Jeśli włączasz API, musisz wygenerować token!'); return; }
        showLoading();
        $.ajax({
            url: `/AllegroPriceHistory/SaveAllegroApiExportSettings?storeId=${storeId}`,
            type: 'POST', contentType: 'application/json',
            data: JSON.stringify({ isEnabled: e, token: t }),
            success: function (resp) { hideLoading(); $('#allegroApiExportModal').modal('hide'); showGlobalUpdate(resp.message); },
            error: function () { hideLoading(); showGlobalNotification('Błąd zapisu.'); }
        });
    });
    $(document).on('click', '#allegroApiExportModal .copy-btn', function () {
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

    restoreCatalogState();
    loadPrices();
    massActions.updateSelectionUI();
});