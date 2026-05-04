document.addEventListener("DOMContentLoaded", function () {

    // ===========================================================
    // STATE
    // ===========================================================
    let allPrices = [];
    let currentScrapId = null;
    let currentlyFilteredPrices = [];
    let chartInstance = null;
    let currentPage = 1;
    const itemsPerPage = 1000;
    let myStoreName = "";

    // Ustawienia producenta - domyślne
    let producerSettings = {
        comparisonSource: 1, // 1 = MapPrice, 0 = StorePrice
        useAmount: false,
        identifierForSimulation: 'EAN',
        usePriceWithDelivery: false,
        thresholds: {
            redDarkPct: 20.00, redPct: 10.00, redLightPct: 1.00,
            greenLightPct: 1.00, greenPct: 10.00, greenDarkPct: 20.00,
            redDarkAmt: 50.00, redAmt: 20.00, redLightAmt: 5.00,
            greenLightAmt: 5.00, greenAmt: 20.00, greenDarkAmt: 50.00
        }
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
        sortDaysViolation: null,
        sortStoresViolating: null,
        sortCeneoSales: null,
        sortSalesTrendAmount: null,
        sortSalesTrendPercent: null
    };

    let positionSlider, offerSlider, myPriceSlider;

    // ===========================================================
    // MASS ACTIONS INTEGRATION
    // ===========================================================
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
        showLoading: showLoading,
        hideLoading: hideLoading,
        showGlobalUpdate: showGlobalUpdate,
        showGlobalNotification: showGlobalNotification,
        onFlagsUpdated: () => loadPrices(),
        onAutomationsUpdated: () => loadPrices()
    });
    massActions.init();

    // ===========================================================
    // HELPERS
    // ===========================================================
    function formatPricePL(value, includeUnit = true) {
        if (value === null || value === undefined || isNaN(parseFloat(value))) return "N/A";
        const numberValue = parseFloat(value);
        const formatted = numberValue.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        return includeUnit ? formatted + ' PLN' : formatted;
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

    function getStockStatusBadge(inStock) {
        if (inStock === true) return '<span class="stock-available">Dostępny</span>';
        else if (inStock === false) return '<span class="stock-unavailable">Niedostępny</span>';
        else return '<span class="BD">Brak danych</span>';
    }

    function getOfferText(count) {
        if (count === 1) return `${count} Oferta`;
        const lastDigit = count % 10;
        if (count > 10 && count < 20) return `${count} Ofert`;
        if ([2, 3, 4].includes(lastDigit)) return `${count} Oferty`;
        return `${count} Ofert`;
    }

    // ===========================================================
    // SLIDERY
    // ===========================================================
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

    // ===========================================================
    // BUCKET LABELS
    // ===========================================================
    const BUCKET_LABELS = {
        'producer-deep-violation': 'Drastyczne naruszenie',
        'producer-violation': 'Naruszenie',
        'producer-minor-below': 'Lekko poniżej',
        'producer-equal': 'Cena zgodna',
        'producer-minor-above': 'Lekko powyżej',
        'producer-above': 'Powyżej',
        'producer-deep-above': 'Mocno powyżej',
        'producer-no-reference': 'Brak ceny ref.',
        'producer-no-competition': 'Brak konkurencji'
    };

    const BUCKET_COLORS = {
        'producer-deep-violation': '#8B0000',
        'producer-violation': '#DC143C',
        'producer-minor-below': '#FF6347',
        'producer-equal': '#4169E1',
        'producer-minor-above': '#90EE90',
        'producer-above': '#228B22',
        'producer-deep-above': '#006400',
        'producer-no-reference': '#BBBBBB',
        'producer-no-competition': '#888888'
    };

    // ===========================================================
    // LOAD DATA
    // ===========================================================
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
                producerSettings = response.producerSettings;

                updateModeBadge();

                if (response.presetName) {
                    const presetSpan = document.querySelector('#presetButton span');
                    const presetText = response.presetName === 'PriceSafari' ? 'Presety - Widok PriceSafari' : 'Presety - ' + response.presetName;
                    if (presetSpan) presetSpan.textContent = presetText;
                    else document.getElementById('presetButton').textContent = presetText;
                }

                allPrices = response.prices.map(p => ({
                    ...p,
                    bucket: p.producerBucket
                }));

                // Slidery range update
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

    function updateModeBadge() {
        const badge = document.getElementById('producerModeBadge');
        if (!badge) return;
        const span = badge.querySelector('span');
        const sourceLabel = producerSettings.comparisonSource === 1 ? 'MAP' : 'Cena sklepu';
        const unitLabel = producerSettings.useAmount ? 'PLN' : '%';
        span.textContent = `Tryb: ${sourceLabel} (${unitLabel})`;
        badge.classList.toggle('amount-mode', producerSettings.useAmount);
    }

    // ===========================================================
    // FLAG COUNTS
    // ===========================================================
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

    // ===========================================================
    // FILTROWANIE
    // ===========================================================
    function filterPricesByCategoryAndColorAndFlag(data) {
        let filtered = data;

        // Buckety
        const selBuckets = Array.from(document.querySelectorAll('.bucketFilter:checked')).map(c => c.value);
        if (selBuckets.length) {
            filtered = filtered.filter(item => selBuckets.includes(item.bucket));
        }

        // Slidery
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

        filtered = filtered.filter(item => (item.storeCount || 0) >= offMin && (item.storeCount || 0) <= offMax);

        filtered = filtered.filter(item => {
            const refP = item.referencePrice != null ? parseFloat(item.referencePrice) : 0;
            if (refP <= 0.01) return true; // brak ceny ref - nie filtrujemy po cenie
            return refP >= priceMin && refP <= priceMax;
        });

        // Flagi
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

        // Filtry zaawansowane
        function check(item, name) {
            switch (name) {
                case 'isBidding': return item.bestCompetitorIsGoogle === false; // konkurent Ceneo - bid niedostępny per produkt, używamy bestCompetitor
                case 'isNew': return item.isNew === true;
                case 'freshViolation': return item.isFreshViolation === true && item.isCurrentlyViolating === true;
                case 'currentlyViolating': return item.isCurrentlyViolating === true;
                case 'compStockAvailable': return item.bestCompetitorIsGoogle === true ? (item.googleInStock === true) : (item.ceneoInStock === true);
                case 'compStockUnavailable': return item.bestCompetitorIsGoogle === true ? (item.googleInStock === false) : (item.ceneoInStock === false);
                default: return false;
            }
        }
        if (selectedAdvancedExcludes.size > 0) filtered = filtered.filter(item => { for (const f of selectedAdvancedExcludes) if (check(item, f)) return false; return true; });
        if (selectedAdvancedIncludes.size > 0) filtered = filtered.filter(item => { for (const f of selectedAdvancedIncludes) if (!check(item, f)) return false; return true; });

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

            // Sortowanie
            if (sortingState.sortName !== null) {
                filtered.sort((a, b) => sortingState.sortName === 'asc' ? a.productName.localeCompare(b.productName) : b.productName.localeCompare(a.productName));
            } else if (sortingState.sortPrice !== null) {
                filtered.sort((a, b) => sortingState.sortPrice === 'asc' ? (b.referencePrice ?? -Infinity) - (a.referencePrice ?? -Infinity) : (a.referencePrice ?? Infinity) - (b.referencePrice ?? Infinity));
            } else if (sortingState.sortDeltaPercent !== null) {
                filtered.sort((a, b) => sortingState.sortDeltaPercent === 'asc' ? (b.deltaPercent ?? -Infinity) - (a.deltaPercent ?? -Infinity) : (a.deltaPercent ?? Infinity) - (b.deltaPercent ?? Infinity));
            } else if (sortingState.sortDeltaAmount !== null) {
                filtered.sort((a, b) => sortingState.sortDeltaAmount === 'asc' ? (b.deltaAbsolute ?? -Infinity) - (a.deltaAbsolute ?? -Infinity) : (a.deltaAbsolute ?? Infinity) - (b.deltaAbsolute ?? Infinity));
            } else if (sortingState.sortDaysViolation !== null) {
                filtered = filtered.filter(i => i.daysOfViolation !== null && i.daysOfViolation !== undefined);
                filtered.sort((a, b) => sortingState.sortDaysViolation === 'asc' ? (a.daysOfViolation ?? Infinity) - (b.daysOfViolation ?? Infinity) : (b.daysOfViolation ?? -Infinity) - (a.daysOfViolation ?? -Infinity));
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
            updateNewProductCount(filtered);
            hideLoading();
        }, 0);
    }

    // ===========================================================
    // RENDER
    // ===========================================================
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

    function createSalesTrendHtml(item) {
        const s = item.salesTrendStatus;
        if (s === 'NoData' || s === null) {
            return `<div class="price-box-column-offers-a">
                <span class="data-channel"><i class="fas fa-chart-line" style="font-size: 15px; color: grey; margin-top:1px;" title="Trend sprzedaży"></i></span>
                <div class="offer-count-box"><p>Brak danych</p></div>
            </div>`;
        }
        let trendText = '';
        if (item.salesDifference !== 0 && item.salesDifference !== null) {
            const sign = item.salesDifference > 0 ? '+' : '';
            const pct = item.salesPercentageChange !== null ? ` (${sign}${item.salesPercentageChange.toFixed(2)}%)` : '';
            trendText = `<p>${sign}${item.salesDifference}${pct}</p>`;
        } else trendText = `<p>Bez zmian</p>`;
        return `<div class="price-box-column-offers-a">
            <span class="data-channel" title="Trend sprzedaży"><img src="/images/Flag-${s}.svg" alt="${s}" style="width:18px; height:18px;" /></span>
            <div class="offer-count-box">${trendText}</div>
        </div>`;
    }

    function createCeneoSalesHtml(item) {
        const tip = 'Ilość zakupionych produktów przez ostatnie 90 dni na Ceneo';
        if (item.ceneoSalesCount > 0) {
            return `<div class="price-box-column-offers-a">
                <span class="data-channel"><i class="fas fa-shopping-cart" style="font-size: 15px; color: grey; margin-top:1px;" title="${tip}"></i></span>
                <div class="offer-count-box"><p>${item.ceneoSalesCount} osb. kupiło</p></div>
            </div>`;
        }
        return `<div class="price-box-column-offers-a">
            <span class="data-channel"><i class="fas fa-shopping-cart" style="font-size: 15px; color: grey; margin-top:1px;" title="${tip}"></i></span>
            <div class="offer-count-box"><p>Brak danych</p></div>
        </div>`;
    }

    function createViolationHistoryHtml(item) {
        if (!item.isCurrentlyViolating) {
            return `<div class="price-box-column-offers-a">
                <span class="data-channel"><i class="fa-solid fa-shield-halved" style="font-size:15px; color:#228B22; margin-top:1px;" title="Brak naruszenia"></i></span>
                <div class="offer-count-box"><span class="violation-no-badge">Bez naruszeń</span></div>
            </div>`;
        }

        if (item.isFreshViolation) {
            return `<div class="price-box-column-offers-a">
                <span class="data-channel"><i class="fa-solid fa-bell" style="font-size:15px; color:#FF6347; margin-top:1px;" title="Świeże naruszenie"></i></span>
                <div class="offer-count-box"><span class="violation-fresh-badge"><i class="fa-solid fa-circle-exclamation"></i> Świeże naruszenie</span></div>
            </div>`;
        }

        const days = item.daysOfViolation;
        let label = 'Naruszenie aktywne';
        if (days !== null && days !== undefined) {
            if (days >= 7) label = `Naruszenie ≥ 7 dni`;
            else if (days >= 1) label = `Naruszenie od ${Math.floor(days)} dni`;
            else if (days > 0) label = `Naruszenie < 1 dnia`;
        }

        return `<div class="price-box-column-offers-a">
            <span class="data-channel"><i class="fa-solid fa-clock-rotate-left" style="font-size:15px; color:#DC143C; margin-top:1px;" title="Trwające naruszenie"></i></span>
            <div class="offer-count-box"><span class="violation-days-badge"><i class="fa-solid fa-triangle-exclamation"></i> ${label}</span></div>
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

        paginated.forEach(item => {
            const highlightedName = highlightMatches(item.productName, productSearchTerm);
            const highlightedStoreName = highlightMatches(item.bestCompetitorStoreName || '', storeSearchTerm);

            const refPrice = item.referencePrice != null ? parseFloat(item.referencePrice) : null;
            const bestComp = item.bestCompetitorPrice != null ? parseFloat(item.bestCompetitorPrice) : null;
            const myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
            const mapPrice = item.mapPrice != null ? parseFloat(item.mapPrice) : null;

            const box = document.createElement('div');
            box.className = 'price-box ' + (item.bucket || 'producer-no-reference');
            box.dataset.detailsUrl = '/PriceHistory/Details?scrapId=' + currentScrapId + '&productId=' + item.productId;
            box.dataset.productId = item.productId;
            box.dataset.productName = item.productName;
            box.addEventListener('click', function () { window.open(this.dataset.detailsUrl, '_blank'); });

            // === GÓRA: nazwa + akcje ===
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
            if (item.isNew) nameDiv.insertAdjacentHTML('beforeend', '<div><span class="badge-new">NEW</span></div>');

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
                apiBox.innerHTML = `${idLabel} ${highlightMatches(idVal, productSearchTerm, 'highlighted-text-yellow')}`;
                apiBox.style.cursor = 'pointer';
                apiBox.title = 'Kliknij, aby skopiować';
                apiBox.addEventListener('click', function (e) {
                    e.stopPropagation();
                    navigator.clipboard.writeText(idVal).then(() => {
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

            // === DANE ===
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

            // Statystyki
            const stats = document.createElement('div');
            stats.className = 'price-box-stats-container';
            const offerCountHtml = `<div class="price-box-column-offers-a">
                <span class="data-channel">
                    ${item.sourceGoogle ? `<img src="/images/GoogleShopping.png" alt="" style="width:15px;height:15px;" />` : ''}
                    ${item.sourceCeneo ? `<img src="/images/Ceneo.png" alt="" style="width:15px;height:15px;" />` : ''}
                </span>
                <div class="offer-count-box">${getOfferText(item.storeCount || 0)}</div>
            </div>`;
            stats.innerHTML = offerCountHtml + createViolationHistoryHtml(item) + createCeneoSalesHtml(item) + createSalesTrendHtml(item);
            priceBoxData.appendChild(stats);

            // === KOLUMNA: Najtańszy konkurent + rozkład ===
            const competitorCol = document.createElement('div');
            competitorCol.className = 'price-box-column';

            if (bestComp != null) {
                const txt = document.createElement('div');
                txt.className = 'price-box-column-text';

                const priceLine = document.createElement('div');
                priceLine.style.cssText = 'display:flex; align-items:center;';
                priceLine.innerHTML = `<span style="font-weight:500; font-size:17px;">${formatPricePL(bestComp)}</span>`;

                const channelIcon = item.bestCompetitorIsGoogle != null
                    ? `<span class="data-channel" style="display:inline-block; vertical-align:middle;"><img src="${item.bestCompetitorIsGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png'}" alt="" style="width:14px; height:14px; margin-right:4px;" /></span>`
                    : '';

                const storeNameDiv = document.createElement('div');
                storeNameDiv.style.cssText = 'display:flex; align-items:center;';
                storeNameDiv.innerHTML = channelIcon + `<span>${highlightedStoreName || ''}</span>`;

                txt.appendChild(priceLine);
                txt.appendChild(storeNameDiv);

                // Rozkład sklepów
                const distrib = document.createElement('div');
                distrib.className = 'stores-distribution';
                if (item.storesBelowReference > 0) {
                    distrib.innerHTML += `<span class="stores-pill below" title="Sklepów poniżej ceny ref."><i class="fa-solid fa-arrow-down"></i> ${item.storesBelowReference} łamie</span>`;
                }
                if (item.storesAtReference > 0) {
                    distrib.innerHTML += `<span class="stores-pill equal" title="Sklepów na poziomie ceny ref."><i class="fa-solid fa-equals"></i> ${item.storesAtReference} zgodnych</span>`;
                }
                if (item.storesAboveReference > 0) {
                    distrib.innerHTML += `<span class="stores-pill above" title="Sklepów powyżej ceny ref."><i class="fa-solid fa-arrow-up"></i> ${item.storesAboveReference} powyżej</span>`;
                }

                txt.appendChild(distrib);
                competitorCol.appendChild(txt);
            } else {
                const txt = document.createElement('div');
                txt.className = 'price-box-column-text';
                txt.innerHTML = '<span style="font-weight:500; font-size:17px;">Brak konkurencji</span><div style="font-size:13px; color:#888;">Produkt nie ma ofert na rynku</div>';
                competitorCol.appendChild(txt);
            }

            priceBoxData.appendChild(competitorCol);

            // === KOLUMNA: Cena referencyjna (MAP) ===
            const refCol = document.createElement('div');
            refCol.className = 'price-box-column';
            const refTxt = document.createElement('div');
            refTxt.className = 'price-box-column-text';
            const refDisplay = document.createElement('div');
            refDisplay.className = 'ref-price-display';

            if (refPrice != null && refPrice > 0) {
                const sourceLabel = item.referenceSource === 'map' ? 'Cena MAP' : 'Cena Twojego sklepu';
                refDisplay.innerHTML = `
                    <span class="ref-label">${sourceLabel}</span>
                    <span class="ref-value">${formatPricePL(refPrice)}</span>
                `;
                if (item.referenceSource === 'map' && myPrice != null && myPrice > 0) {
                    refDisplay.innerHTML += `<span class="ref-source">Twoja oferta: ${formatPricePL(myPrice)}</span>`;
                }
                if (item.referenceSource === 'store' && mapPrice != null && mapPrice > 0) {
                    refDisplay.innerHTML += `<span class="ref-source">MAP w katalogu: ${formatPricePL(mapPrice)}</span>`;
                }
            } else {
                let missingMsg = '';
                if (producerSettings.comparisonSource === 1) missingMsg = 'Brak ceny MAP w katalogu';
                else missingMsg = 'Brak Twojej oferty';
                refDisplay.innerHTML = `
                    <span class="ref-label">Cena referencyjna</span>
                    <span class="ref-missing"><i class="fa-solid fa-circle-exclamation"></i> ${missingMsg}</span>
                `;
                if (mapPrice != null && mapPrice > 0) refDisplay.innerHTML += `<span class="ref-source">MAP w katalogu: ${formatPricePL(mapPrice)}</span>`;
                if (myPrice != null && myPrice > 0) refDisplay.innerHTML += `<span class="ref-source">Twoja oferta: ${formatPricePL(myPrice)}</span>`;
            }
            refTxt.appendChild(refDisplay);
            refCol.appendChild(refTxt);
            priceBoxData.appendChild(refCol);

            // === KOLUMNA: Delta ===
            const deltaCol = document.createElement('div');
            deltaCol.className = 'price-box-column-action';
            const deltaTxt = document.createElement('div');
            deltaTxt.className = 'price-box-column-text';

            if (item.bucket === 'producer-no-reference') {
                deltaTxt.innerHTML = `<span style="color:#888; font-style:italic;">Nie można obliczyć - brak ceny referencyjnej</span>`;
            } else if (item.bucket === 'producer-no-competition') {
                deltaTxt.innerHTML = `<span style="color:#888; font-style:italic;">Brak ofert konkurencji</span>`;
            } else if (item.deltaAbsolute != null) {
                const deltaA = parseFloat(item.deltaAbsolute);
                const deltaP = item.deltaPercent != null ? parseFloat(item.deltaPercent) : null;
                const sign = deltaA > 0 ? '+' : '';
                const arrow = deltaA > 0 ? '<i class="fa-solid fa-arrow-up"></i>' : (deltaA < 0 ? '<i class="fa-solid fa-arrow-down"></i>' : '<i class="fa-solid fa-equals"></i>');

                deltaTxt.innerHTML = `
                    <div class="delta-badge ${item.bucket}">${arrow} ${sign}${formatPricePL(deltaA, false)} PLN</div>
                    ${deltaP != null ? `<div style="margin-top:4px; font-size:13px; color:#555;">${sign}${deltaP.toFixed(2)}% wzgl. ceny referencyjnej</div>` : ''}
                    <div style="margin-top:6px; font-size:13px; color:#222; font-weight:500;">${BUCKET_LABELS[item.bucket] || ''}</div>
                `;
            }

            deltaCol.appendChild(deltaTxt);
            priceBoxData.appendChild(deltaCol);

            box.appendChild(priceBoxSpace);
            box.appendChild(priceBoxData);
            c.appendChild(box);
        });

        renderPagination(data.length);

        // Lazy load
        const lazyImgs = document.querySelectorAll('.lazy-load');
        const timers = new Map();
        const obs = new IntersectionObserver((entries, observer) => {
            entries.forEach(entry => {
                const img = entry.target;
                const idx = [...lazyImgs].indexOf(img);
                if (entry.isIntersecting) {
                    const t = setTimeout(() => { loadWithNeighbors(idx); observer.unobserve(img); timers.delete(img); }, 100);
                    timers.set(img, t);
                } else if (timers.has(img)) { clearTimeout(timers.get(img)); timers.delete(img); }
            });
        }, { root: null, rootMargin: '50px', threshold: 0.01 });

        function loadWithNeighbors(idx) {
            const range = 6;
            const s = Math.max(0, idx - range);
            const e = Math.min(lazyImgs.length - 1, idx + range);
            for (let i = s; i <= e; i++) {
                const im = lazyImgs[i];
                if (!im.src) { im.src = im.dataset.src; im.onload = () => im.classList.add('loaded'); }
            }
        }
        lazyImgs.forEach(im => obs.observe(im));

        document.getElementById('displayedProductCount').textContent = data.length;
    }

    // ===========================================================
    // CHART
    // ===========================================================
    function renderChart(data) {
        const ctx = document.getElementById('colorChart').getContext('2d');
        const counts = {
            'producer-deep-violation': 0,
            'producer-violation': 0,
            'producer-minor-below': 0,
            'producer-equal': 0,
            'producer-minor-above': 0,
            'producer-above': 0,
            'producer-deep-above': 0,
            'producer-no-reference': 0,
            'producer-no-competition': 0
        };
        data.forEach(i => { if (counts.hasOwnProperty(i.bucket)) counts[i.bucket]++; });

        const labels = Object.keys(counts).map(k => BUCKET_LABELS[k]);
        const values = Object.values(counts);
        const colors = Object.keys(counts).map(k => BUCKET_COLORS[k]);

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
                    plugins: { legend: { display: false }, tooltip: { callbacks: { label: c => c.label + ': ' + c.parsed } } }
                }
            });
        }
    }

    function updateBucketCountsUI(data) {
        const counts = {
            'producer-deep-violation': 0, 'producer-violation': 0, 'producer-minor-below': 0,
            'producer-equal': 0, 'producer-minor-above': 0, 'producer-above': 0, 'producer-deep-above': 0,
            'producer-no-reference': 0, 'producer-no-competition': 0
        };
        data.forEach(i => { if (counts.hasOwnProperty(i.bucket)) counts[i.bucket]++; });

        const map = {
            'producer-deep-violation': 'bucketDeepViolation',
            'producer-violation': 'bucketViolation',
            'producer-minor-below': 'bucketMinorBelow',
            'producer-equal': 'bucketEqual',
            'producer-minor-above': 'bucketMinorAbove',
            'producer-above': 'bucketAbove',
            'producer-deep-above': 'bucketDeepAbove',
            'producer-no-reference': 'bucketNoReference',
            'producer-no-competition': 'bucketNoCompetition'
        };

        for (const [b, eid] of Object.entries(map)) {
            const lbl = document.querySelector(`label[for="${eid}"]`);
            if (lbl) {
                const txt = lbl.textContent.split('(')[0].trim();
                lbl.textContent = `${txt} (${counts[b] || 0})`;
            }
        }
    }

    // ===========================================================
    // PRODUCER SETTINGS MODAL + WALIDACJA
    // ===========================================================
    document.getElementById('openProducerSettingsBtn').addEventListener('click', function () {
        loadProducerSettingsToModal();
        $('#producerSettingsModal').modal('show');
    });

    function loadProducerSettingsToModal() {
        $.get(`/PriceHistory/GetProducerSettings?storeId=${storeId}`, function (data) {
            $('#identifierForSimulationInput').val(data.identifierForSimulation || 'EAN');
            $('#comparisonSourceInput').val(data.comparisonSource);
            $('#useAmountInput').val(data.useAmount.toString());

            $('#thrRedDarkPct').val(data.redDarkPct);
            $('#thrRedPct').val(data.redPct);
            $('#thrRedLightPct').val(data.redLightPct);
            $('#thrGreenLightPct').val(data.greenLightPct);
            $('#thrGreenPct').val(data.greenPct);
            $('#thrGreenDarkPct').val(data.greenDarkPct);

            $('#thrRedDarkAmt').val(data.redDarkAmt);
            $('#thrRedAmt').val(data.redAmt);
            $('#thrRedLightAmt').val(data.redLightAmt);
            $('#thrGreenLightAmt').val(data.greenLightAmt);
            $('#thrGreenAmt').val(data.greenAmt);
            $('#thrGreenDarkAmt').val(data.greenDarkAmt);

            toggleThresholdBlocks();
            validateThresholds();
        });
    }

    function toggleThresholdBlocks() {
        const useAmt = $('#useAmountInput').val() === 'true';
        $('#thresholdsBlock_pct').toggle(!useAmt);
        $('#thresholdsBlock_amt').toggle(useAmt);
    }

    $('#useAmountInput').on('change', function () {
        toggleThresholdBlocks();
        validateThresholds();
    });

    // Live walidacja
    $(document).on('input', '.threshold-input', function () {
        validateThresholds();
    });

    /**
     * Walidacja progów - sprawdza:
     *  - wszystkie wartości >= 0
     *  - kolejność: light <= normal <= dark (oddzielnie below i above)
     *  - waliduje obie strony (% i PLN), bo nieaktywna i tak musi być spójna
     * Ustawia czerwone obramowania, komunikaty i blokuje przycisk Save.
     */
    function validateThresholds() {
        const get = id => parseFloat(($('#' + id).val() || '').replace(',', '.'));

        const groups = {
            'below-pct': {
                fields: [
                    { id: 'thrRedLightPct', level: 1 },
                    { id: 'thrRedPct', level: 2 },
                    { id: 'thrRedDarkPct', level: 3 }
                ], errEl: 'errBelowPct', label: 'Progi „poniżej" (%)'
            },
            'above-pct': {
                fields: [
                    { id: 'thrGreenLightPct', level: 1 },
                    { id: 'thrGreenPct', level: 2 },
                    { id: 'thrGreenDarkPct', level: 3 }
                ], errEl: 'errAbovePct', label: 'Progi „powyżej" (%)'
            },
            'below-amt': {
                fields: [
                    { id: 'thrRedLightAmt', level: 1 },
                    { id: 'thrRedAmt', level: 2 },
                    { id: 'thrRedDarkAmt', level: 3 }
                ], errEl: 'errBelowAmt', label: 'Progi „poniżej" (PLN)'
            },
            'above-amt': {
                fields: [
                    { id: 'thrGreenLightAmt', level: 1 },
                    { id: 'thrGreenAmt', level: 2 },
                    { id: 'thrGreenDarkAmt', level: 3 }
                ], errEl: 'errAboveAmt', label: 'Progi „powyżej" (PLN)'
            }
        };

        let allValid = true;
        const allErrors = [];

        // Zerujemy stan
        $('.threshold-input').removeClass('input-error');
        $('.error-message-row').removeClass('visible').text('');

        Object.entries(groups).forEach(([key, group]) => {
            const errs = [];
            const values = group.fields.map(f => ({ ...f, val: get(f.id) }));

            // 1. NaN / ujemne
            values.forEach(v => {
                if (isNaN(v.val) || v.val < 0) {
                    $('#' + v.id).addClass('input-error');
                    errs.push(`Wartość musi być liczbą >= 0`);
                    allValid = false;
                }
            });

            // 2. Kolejność: light <= normal <= dark
            const light = values.find(v => v.level === 1);
            const normal = values.find(v => v.level === 2);
            const dark = values.find(v => v.level === 3);

            if (!isNaN(light.val) && !isNaN(normal.val) && light.val > normal.val) {
                $('#' + light.id).addClass('input-error');
                $('#' + normal.id).addClass('input-error');
                errs.push(`Próg „lekki" (${light.val}) nie może być większy niż „normalny" (${normal.val})`);
                allValid = false;
            }
            if (!isNaN(normal.val) && !isNaN(dark.val) && normal.val > dark.val) {
                $('#' + normal.id).addClass('input-error');
                $('#' + dark.id).addClass('input-error');
                errs.push(`Próg „normalny" (${normal.val}) nie może być większy niż „mocny" (${dark.val})`);
                allValid = false;
            }

            if (errs.length > 0) {
                const uniqErrs = [...new Set(errs)];
                $('#' + group.errEl).addClass('visible').html(uniqErrs.map(e => `<i class="fa-solid fa-circle-exclamation"></i> ${e}`).join('<br>'));
                allErrors.push(group.label + ': ' + uniqErrs.join('; '));
            }
        });

        // Zaktualizuj stan przycisku Save
        const saveBtn = document.getElementById('saveProducerSettingsBtn');
        if (saveBtn) {
            saveBtn.disabled = !allValid;
            saveBtn.style.opacity = allValid ? '1' : '0.5';
            saveBtn.style.cursor = allValid ? 'pointer' : 'not-allowed';
            saveBtn.title = allValid ? '' : 'Popraw błędy walidacji aby zapisać';
        }

        return allValid;
    }

    document.getElementById('saveProducerSettingsBtn').addEventListener('click', function () {
        if (!validateThresholds()) {
            showGlobalNotification('<p style="font-weight:bold;">Walidacja</p><p>Popraw błędy w formularzu przed zapisem.</p>');
            return;
        }

        const get = id => parseFloat(($('#' + id).val() || '').replace(',', '.'));

        const payload = {
            StoreId: storeId,
            IdentifierForSimulation: $('#identifierForSimulationInput').val(),
            ProducerComparisonSource: parseInt($('#comparisonSourceInput').val()),
            ProducerUseAmount: $('#useAmountInput').val() === 'true',
            ProducerThresholdRedDarkPercent: get('thrRedDarkPct'),
            ProducerThresholdRedPercent: get('thrRedPct'),
            ProducerThresholdRedLightPercent: get('thrRedLightPct'),
            ProducerThresholdGreenLightPercent: get('thrGreenLightPct'),
            ProducerThresholdGreenPercent: get('thrGreenPct'),
            ProducerThresholdGreenDarkPercent: get('thrGreenDarkPct'),
            ProducerThresholdRedDarkAmount: get('thrRedDarkAmt'),
            ProducerThresholdRedAmount: get('thrRedAmt'),
            ProducerThresholdRedLightAmount: get('thrRedLightAmt'),
            ProducerThresholdGreenLightAmount: get('thrGreenLightAmt'),
            ProducerThresholdGreenAmount: get('thrGreenAmt'),
            ProducerThresholdGreenDarkAmount: get('thrGreenDarkAmt')
        };

        fetch('/PriceHistory/SaveProducerSettings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        })
            .then(r => r.json())
            .then(data => {
                if (data.success) {
                    showGlobalUpdate('<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Ustawienia zapisane</p><p>Przeliczam dane z nowymi progami...</p>');
                    $('#producerSettingsModal').modal('hide');
                    loadPrices();
                } else {
                    showGlobalNotification('<p>' + (data.message || 'Błąd zapisu') + '</p>');
                }
            })
            .catch(err => {
                console.error(err);
                showGlobalNotification('<p>Błąd zapisu ustawień. Sprawdź konsolę.</p>');
            });
    });

    // ===========================================================
    // SORTOWANIE
    // ===========================================================
    function resetSortingStates(except) {
        Object.keys(sortingState).forEach(k => { if (k !== except) sortingState[k] = null; });
        updateSortButtonVisuals();
    }

    function getDefaultButtonLabel(key) {
        switch (key) {
            case 'sortName': return 'A-Z';
            case 'sortPrice': return 'Cena MAP';
            case 'sortDeltaPercent': return 'Delta %';
            case 'sortDeltaAmount': return 'Delta PLN';
            case 'sortDaysViolation': return 'Dni naruszenia';
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

    ['sortName', 'sortPrice', 'sortDeltaPercent', 'sortDeltaAmount', 'sortDaysViolation', 'sortStoresViolating',
        'sortCeneoSales', 'sortSalesTrendAmount', 'sortSalesTrendPercent'].forEach(bindSortButton);

    // Restore sorting
    const storedSort = localStorage.getItem('priceHistoryProducerSorting_' + storeId);
    if (storedSort) {
        try { sortingState = { ...sortingState, ...JSON.parse(storedSort) }; updateSortButtonVisuals(); }
        catch (e) { localStorage.removeItem('priceHistoryProducerSorting_' + storeId); }
    }

    // ===========================================================
    // EVENT BINDING
    // ===========================================================
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

    // ===========================================================
    // EKSPORT EXCEL
    // ===========================================================
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
                { header: 'Producent', key: 'producer', width: 20 },
                { header: 'Nazwa produktu', key: 'name', width: 40 },
                { header: 'Cena referencyjna', key: 'ref', width: 14, style: { numFmt: '0.00' } },
                { header: 'Źródło ref.', key: 'refSource', width: 16 },
                { header: 'Najtańsza konkurencja', key: 'best', width: 14, style: { numFmt: '0.00' } },
                { header: 'Sklep konkurenta', key: 'bestStore', width: 20 },
                { header: 'Delta (PLN)', key: 'deltaA', width: 12, style: { numFmt: '0.00' } },
                { header: 'Delta (%)', key: 'deltaP', width: 10, style: { numFmt: '0.00' } },
                { header: 'Status', key: 'bucket', width: 22 },
                { header: 'Sklepów łamiących', key: 'below', width: 14 },
                { header: 'Sklepów zgodnych', key: 'eq', width: 14 },
                { header: 'Sklepów powyżej', key: 'above', width: 14 },
                { header: 'Liczba ofert łącznie', key: 'storeCount', width: 14 },
                { header: 'Naruszenie obecne', key: 'isViolating', width: 14 },
                { header: 'Świeże naruszenie', key: 'isFresh', width: 14 },
                { header: 'Dni naruszenia', key: 'days', width: 14, style: { numFmt: '0.00' } },
                { header: 'Sprzedaż Ceneo (90d)', key: 'sales', width: 14 }
            ];

            ws.getRow(1).font = { bold: true };
            ws.getRow(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFE0E0E0' } };

            currentlyFilteredPrices.forEach(item => {
                ws.addRow({
                    ean: item.ean || '',
                    sku: item.producerCode || '',
                    producer: item.producer || '',
                    name: item.productName || '',
                    ref: item.referencePrice,
                    refSource: item.referenceSource === 'map' ? 'MAP' : (item.referenceSource === 'store' ? 'Sklep' : 'Brak'),
                    best: item.bestCompetitorPrice,
                    bestStore: item.bestCompetitorStoreName || '',
                    deltaA: item.deltaAbsolute,
                    deltaP: item.deltaPercent,
                    bucket: BUCKET_LABELS[item.bucket] || '',
                    below: item.storesBelowReference,
                    eq: item.storesAtReference,
                    above: item.storesAboveReference,
                    storeCount: item.storeCount,
                    isViolating: item.isCurrentlyViolating ? 'TAK' : 'NIE',
                    isFresh: item.isFreshViolation ? 'TAK' : 'NIE',
                    days: item.daysOfViolation,
                    sales: item.ceneoSalesCount
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

    // ===========================================================
    // API EXPORT MODAL
    // ===========================================================
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
        if (e && (!t || t.trim() === '')) { alert('Jeśli włączasz API, musisz wygenerować token!'); return; }
        showLoading();
        $.ajax({
            url: `/PriceHistory/SaveApiExportSettings?storeId=${storeId}`,
            type: 'POST', contentType: 'application/json',
            data: JSON.stringify({ isEnabled: e, token: t }),
            success: function (resp) { hideLoading(); $('#apiExportModal').modal('hide'); showGlobalUpdate(resp.message); },
            error: function () { hideLoading(); showGlobalNotification('Błąd zapisu.'); }
        });
    });
    $('.copy-btn').click(function () {
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

    // ===========================================================
    // INIT
    // ===========================================================
    loadPrices();
    massActions.updateSelectionUI();
});