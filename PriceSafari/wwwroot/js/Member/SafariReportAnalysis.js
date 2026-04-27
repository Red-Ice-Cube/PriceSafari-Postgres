document.addEventListener("DOMContentLoaded", function () {

    // ============================================================
    // STATE & CONFIG
    // ============================================================
    const config = window.safariConfig || {};
    const reportId = config.reportId;
    const storeId = config.storeId;
    let regionId = config.regionId;

    let allProducts = [];
    let currentlyFilteredProducts = [];
    let myStoreName = config.myStoreName || '';

    let setSafariPrice1 = parseFloat(config.setSafariPrice1) || 2.00;
    let setSafariPrice2 = parseFloat(config.setSafariPrice2) || 2.00;
    let usePriceDifference = !!config.usePriceDiffSafari;
    let identifierForSimulation = config.identifierForSimulation || 'EAN';

    const flags = config.flags || [];

    let chartInstance = null;
    let chartType = null;
    let currentChartType = 'classification';

    let currentPage = 1;
    const itemsPerPage = 1000;

    let selectedColors = new Set();
    let selectedFlagsInclude = new Set();
    let selectedFlagsExclude = new Set();

    let sortingState = {
        sortName: null, sortPrice: null,
        sortDifferenceAmount: null, sortDifferencePercent: null,
        sortMarginAmount: null, sortMarginPercent: null,
        sortOfferCount: null
    };

    let positionSlider, offerSlider, myPriceSlider, compPriceSlider;

    // ============================================================
    // HELPERS
    // ============================================================
    const showLoading = () => { const el = document.getElementById('loadingOverlay'); if (el) el.style.display = 'flex'; };
    const hideLoading = () => { const el = document.getElementById('loadingOverlay'); if (el) el.style.display = 'none'; };

    function formatPricePL(value, includeUnit = true) {
        if (value === null || value === undefined || isNaN(parseFloat(value))) return "N/A";
        const formatted = parseFloat(value).toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        return includeUnit ? formatted + ' PLN' : formatted;
    }

    function formatOriginalPrice(value, currency) {
        if (value === null || value === undefined) return null;
        const formatted = parseFloat(value).toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        return formatted + ' ' + (currency || '');
    }

    function debounce(func, wait) {
        let timeout;
        return function (...args) {
            clearTimeout(timeout);
            timeout = setTimeout(() => func.apply(this, args), wait);
        };
    }

    function hexToRgba(hex, alpha) {
        if (!hex) return `rgba(80,80,80,${alpha})`;
        let r = 0, g = 0, b = 0;
        if (hex.length === 4) {
            r = parseInt(hex[1] + hex[1], 16);
            g = parseInt(hex[2] + hex[2], 16);
            b = parseInt(hex[3] + hex[3], 16);
        } else if (hex.length === 7) {
            r = parseInt(hex[1] + hex[2], 16);
            g = parseInt(hex[3] + hex[4], 16);
            b = parseInt(hex[5] + hex[6], 16);
        }
        return `rgba(${r}, ${g}, ${b}, ${alpha})`;
    }

    function highlightMatches(text, term, customClass) {
        if (!term || !text) return text || '';
        const escaped = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        const regex = new RegExp(escaped, 'gi');
        const cls = customClass || 'highlighted-text';
        return text.replace(regex, m => `<span class="${cls}">${m}</span>`);
    }

    let notifyTimeoutId, updateTimeoutId;
    function showGlobalNotification(message) {
        const notif = document.getElementById('globalNotification');
        if (!notif) return;
        if (notifyTimeoutId) clearTimeout(notifyTimeoutId);
        notif.innerHTML = message;
        notif.style.display = 'block';
        notifyTimeoutId = setTimeout(() => notif.style.display = 'none', 4000);
    }
    function showGlobalUpdate(message) {
        const notif = document.getElementById('globalUpdate');
        if (!notif) return;
        if (updateTimeoutId) clearTimeout(updateTimeoutId);
        notif.innerHTML = message;
        notif.style.display = 'block';
        updateTimeoutId = setTimeout(() => notif.style.display = 'none', 4000);
    }

    function getColorClass(product) {
        if (!product.myCalculatedPrice || product.myCalculatedPrice <= 0) return 'prNoOffer';
        if (!product.bestCalculatedPrice || product.bestCalculatedPrice <= 0) return 'prGray';

        const value = usePriceDifference
            ? (product.priceDifference != null ? product.priceDifference : 0)
            : (product.percentageDifference != null ? product.percentageDifference : 0);

        if (value <= -setSafariPrice1) return 'prIdeal';
        if (value >= setSafariPrice2) return 'prToHigh';
        return 'prGray';
    }

    function recomputeColorClasses() {
        allProducts.forEach(p => p.colorClass = getColorClass(p));
    }

    function pluralOferta(n) {
        if (n === 1) return 'oferta';
        if (n >= 2 && n <= 4) return 'oferty';
        return 'ofert';
    }

    function pluralKraj(n) {
        if (n === 1) return 'kraj';
        if (n >= 2 && n <= 4) return 'kraje';
        return 'krajów';
    }

    // ============================================================
    // OFFER STATS HELPER — oblicza ile ofert tańszych/droższych
    // ============================================================
    function computeOfferStats(p) {
        const competitors = (p.allOffers || []).filter(o => !o.isMe);
        const myPrice = p.myCalculatedPrice;
        const purchasePrice = p.marginPrice;

        let cheaperThanMe = 0;
        let moreExpensiveThanMe = 0;
        let belowPurchase = 0;
        let abovePurchase = 0;

        competitors.forEach(o => {
            if (myPrice && myPrice > 0) {
                if (o.calculatedPrice < myPrice) cheaperThanMe++;
                else if (o.calculatedPrice > myPrice) moreExpensiveThanMe++;
            }
            if (purchasePrice && purchasePrice > 0) {
                if (o.calculatedPrice < purchasePrice) belowPurchase++;
                else abovePurchase++;
            }
        });

        return { cheaperThanMe, moreExpensiveThanMe, belowPurchase, abovePurchase, totalCompetitors: competitors.length };
    }

    // ============================================================
    // SLIDERS
    // ============================================================
    function initSliders() {
        const positionEl = document.getElementById('positionRangeSlider');
        const positionDisplay = document.getElementById('positionRange');
        noUiSlider.create(positionEl, {
            start: [1, 200], connect: true,
            range: { min: 1, max: 200 }, step: 1,
            format: wNumb({ decimals: 0 })
        });
        positionEl.noUiSlider.on('update', (v) => positionDisplay.textContent = `Pozycja ${v[0]} - ${v[1]}`);
        positionEl.noUiSlider.on('change', filterPricesAndUpdateUI);
        positionSlider = positionEl;

        const offerEl = document.getElementById('offerRangeSlider');
        const offerDisplay = document.getElementById('offerRange');
        noUiSlider.create(offerEl, {
            start: [1, 100], connect: true,
            range: { min: 1, max: 100 }, step: 1,
            format: wNumb({ decimals: 0 })
        });
        offerEl.noUiSlider.on('update', (v) => {
            offerDisplay.textContent = `${v[0]} ${pluralOferta(parseInt(v[0]))} - ${v[1]} ${pluralOferta(parseInt(v[1]))}`;
        });
        offerEl.noUiSlider.on('change', filterPricesAndUpdateUI);
        offerSlider = offerEl;

        const myPriceEl = document.getElementById('myPriceRangeSlider');
        const myPriceDisplay = document.getElementById('myPriceRange');
        noUiSlider.create(myPriceEl, {
            start: [0, 10000], connect: true,
            range: { min: 0, max: 10000 },
            format: wNumb({ decimals: 2, thousand: ' ', suffix: ' PLN' })
        });
        myPriceEl.noUiSlider.on('update', (v) => myPriceDisplay.textContent = v.join(' - '));
        myPriceEl.noUiSlider.on('change', filterPricesAndUpdateUI);
        myPriceSlider = myPriceEl;

        const compPriceEl = document.getElementById('compPriceRangeSlider');
        const compPriceDisplay = document.getElementById('compPriceRange');
        noUiSlider.create(compPriceEl, {
            start: [0, 10000], connect: true,
            range: { min: 0, max: 10000 },
            format: wNumb({ decimals: 2, thousand: ' ', suffix: ' PLN' })
        });
        compPriceEl.noUiSlider.on('update', (v) => compPriceDisplay.textContent = v.join(' - '));
        compPriceEl.noUiSlider.on('change', filterPricesAndUpdateUI);
        compPriceSlider = compPriceEl;
    }

    function updateSlidersRanges() {
        const pos = allProducts.map(p => p.myRank).filter(p => p && p > 0);
        const maxPos = pos.length > 0 ? Math.max(...pos, 1) : 60;
        positionSlider.noUiSlider.updateOptions({ range: { min: 1, max: Math.max(maxPos, 1) } });
        positionSlider.noUiSlider.set([1, maxPos]);

        const offers = allProducts.map(p => p.offerCount || 0);
        const maxOffers = offers.length > 0 ? Math.max(...offers, 1) : 1;
        offerSlider.noUiSlider.updateOptions({ range: { min: 1, max: Math.max(maxOffers, 1) } });
        offerSlider.noUiSlider.set([1, maxOffers]);

        const myPrices = allProducts.map(p => p.myCalculatedPrice).filter(p => p && p > 0);
        if (myPrices.length > 0) {
            const minP = Math.floor(Math.min(...myPrices));
            const maxP = Math.ceil(Math.max(...myPrices));
            myPriceSlider.noUiSlider.updateOptions({ range: { min: minP, max: Math.max(maxP, minP + 1) } });
            myPriceSlider.noUiSlider.set([minP, maxP]);
        }

        const compPrices = allProducts.map(p => p.bestCalculatedPrice).filter(p => p && p > 0);
        if (compPrices.length > 0) {
            const minP = Math.floor(Math.min(...compPrices));
            const maxP = Math.ceil(Math.max(...compPrices));
            compPriceSlider.noUiSlider.updateOptions({ range: { min: minP, max: Math.max(maxP, minP + 1) } });
            compPriceSlider.noUiSlider.set([minP, maxP]);
        }
    }

    // ============================================================
    // FILTERING
    // ============================================================
    function filterProducts(data) {
        let filtered = [...data];

        if (selectedColors.size > 0) {
            filtered = filtered.filter(p => selectedColors.has(p.colorClass));
        }

        const productSearch = document.getElementById('productSearch').value.trim();
        if (productSearch) {
            const sanitized = productSearch.replace(/[^a-zA-Z0-9\s.-]/g, '').toLowerCase().replace(/\s+/g, '');
            filtered = filtered.filter(p => {
                let identifierVal = '';
                switch (identifierForSimulation) {
                    case 'ID': identifierVal = p.externalId ? String(p.externalId) : ''; break;
                    case 'ProducerCode': identifierVal = p.producerCode || ''; break;
                    default: identifierVal = p.ean || '';
                }
                const combined = ((p.productName || '') + ' ' + identifierVal)
                    .toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
                return combined.includes(sanitized);
            });
        }

        const storeSearch = document.getElementById('storeSearch').value.trim();
        if (storeSearch) {
            const sanitized = storeSearch.replace(/[^a-zA-Z0-9\s.-]/g, '').toLowerCase().replace(/\s+/g, '');
            filtered = filtered.filter(p => {
                const stores = (p.allOffers || [])
                    .map(o => (o.storeName || '').toLowerCase().replace(/\s+/g, ''))
                    .join(' ');
                return stores.includes(sanitized);
            });
        }

        const selectedProducer = document.getElementById('producerFilterDropdown').value;
        if (selectedProducer) {
            filtered = filtered.filter(p => p.producer === selectedProducer);
        }

        const offerVals = offerSlider.noUiSlider.get();
        const offerMin = parseInt(offerVals[0]), offerMax = parseInt(offerVals[1]);
        filtered = filtered.filter(p => {
            const c = p.offerCount || 0;
            return c >= offerMin && c <= offerMax;
        });

        const parseSlider = (s) => parseFloat(s.replace(' PLN', '').replace(/\s/g, '').replace(',', '.'));

        const mp = myPriceSlider.noUiSlider.get();
        const myPriceMin = parseSlider(mp[0]), myPriceMax = parseSlider(mp[1]);
        filtered = filtered.filter(p => !p.myCalculatedPrice || (p.myCalculatedPrice >= myPriceMin && p.myCalculatedPrice <= myPriceMax));

        const cp = compPriceSlider.noUiSlider.get();
        const compPriceMin = parseSlider(cp[0]), compPriceMax = parseSlider(cp[1]);
        filtered = filtered.filter(p => !p.bestCalculatedPrice || (p.bestCalculatedPrice >= compPriceMin && p.bestCalculatedPrice <= compPriceMax));

        const pv = positionSlider.noUiSlider.get();
        const posMin = parseInt(pv[0]), posMax = parseInt(pv[1]);
        filtered = filtered.filter(p => !p.myRank || p.myRank === 0 || (p.myRank >= posMin && p.myRank <= posMax));

        if (selectedFlagsExclude.size > 0) {
            filtered = filtered.filter(p => {
                if (selectedFlagsExclude.has('noFlag') && (!p.flagIds || p.flagIds.length === 0)) return false;
                for (const fid of selectedFlagsExclude) {
                    if (fid !== 'noFlag' && p.flagIds && p.flagIds.includes(parseInt(fid))) return false;
                }
                return true;
            });
        }

        if (selectedFlagsInclude.size > 0) {
            filtered = filtered.filter(p => {
                if (selectedFlagsInclude.has('noFlag') && (!p.flagIds || p.flagIds.length === 0)) return true;
                return p.flagIds && p.flagIds.some(fid => selectedFlagsInclude.has(String(fid)));
            });
        }

        return filtered;
    }

    function sortProducts(data) {
        const arr = [...data];
        const cmp = (a, b, dir) => dir === 'asc' ? a - b : b - a;
        const cmpStr = (a, b, dir) => dir === 'asc' ? a.localeCompare(b) : b.localeCompare(a);

        if (sortingState.sortName) arr.sort((a, b) => cmpStr(a.productName || '', b.productName || '', sortingState.sortName));
        else if (sortingState.sortPrice) arr.sort((a, b) => cmp(a.myCalculatedPrice ?? Infinity, b.myCalculatedPrice ?? Infinity, sortingState.sortPrice));
        else if (sortingState.sortDifferenceAmount) arr.sort((a, b) => cmp(a.priceDifference ?? Infinity, b.priceDifference ?? Infinity, sortingState.sortDifferenceAmount));
        else if (sortingState.sortDifferencePercent) arr.sort((a, b) => cmp(a.percentageDifference ?? Infinity, b.percentageDifference ?? Infinity, sortingState.sortDifferencePercent));
        else if (sortingState.sortMarginAmount) arr.sort((a, b) => cmp(a.marginAmount ?? Infinity, b.marginAmount ?? Infinity, sortingState.sortMarginAmount));
        else if (sortingState.sortMarginPercent) arr.sort((a, b) => cmp(a.marginPercentage ?? Infinity, b.marginPercentage ?? Infinity, sortingState.sortMarginPercent));
        else if (sortingState.sortOfferCount) arr.sort((a, b) => cmp(a.offerCount ?? 0, b.offerCount ?? 0, sortingState.sortOfferCount));

        return arr;
    }

    function filterPricesAndUpdateUI(reset = true) {
        if (reset) currentPage = 1;
        showLoading();
        setTimeout(() => {
            recomputeColorClasses();
            const filtered = filterProducts(allProducts);
            const sorted = sortProducts(filtered);
            currentlyFilteredProducts = sorted;

            renderPrices(sorted);
            renderCurrentChart(sorted);
            updateColorCounts();
            updateFlagCounts(sorted);

            document.getElementById('displayedProductCount').textContent = sorted.length;
            document.getElementById('modalProductCount').textContent = sorted.length;
            const totalOffers = sorted.reduce((sum, p) => sum + (p.offerCount || 0), 0);
            document.getElementById('modalOfferCount').textContent = totalOffers;
            hideLoading();
        }, 0);
    }

    // ============================================================
    // CHARTS
    // ============================================================
    function destroyChart() {
        if (chartInstance) {
            try {
                if (chartType === 'echarts' && chartInstance.dispose) chartInstance.dispose();
                else if (chartInstance.destroy) chartInstance.destroy();
            } catch (e) { /* swallow */ }
            chartInstance = null;
        }
        const container = document.querySelector('.side-chart-box');
        if (container) {
            container.innerHTML = '<canvas id="mainChart"></canvas>';
        }
    }

    function renderCurrentChart(data) {
        if (currentChartType === 'classification') renderClassificationChart(data);
        else if (currentChartType === 'countries') renderCountriesChart(data);
    }

    // --- Donut: Klasyfikacja ---
    function renderClassificationChart(data) {
        destroyChart();
        chartType = 'chartjs';
        const ctx = document.getElementById('mainChart').getContext('2d');

        const counts = { prIdeal: 0, prGray: 0, prToHigh: 0, prNoOffer: 0 };
        data.forEach(p => { counts[p.colorClass] = (counts[p.colorClass] || 0) + 1; });

        chartInstance = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: ['Tańsi (Ty)', 'Podobna cena', 'Drożsi (Ty)', 'Brak oferty'],
                datasets: [{
                    data: [counts.prIdeal, counts.prGray, counts.prToHigh, counts.prNoOffer],
                    backgroundColor: [
                        'rgba(13, 110, 253, 0.85)',
                        'rgba(40, 40, 40, 0.7)',
                        'rgba(171, 37, 32, 0.85)',
                        'rgba(220, 220, 220, 0.85)'
                    ],
                    borderWidth: 0,
                    hoverOffset: 0,
                    hoverBorderWidth: 0
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: '62%',
                animation: { duration: 400 },
                hover: { mode: null },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        backgroundColor: 'rgba(34, 34, 34, 0.95)',
                        padding: 8,
                        cornerRadius: 4,
                        callbacks: { label: c => `${c.label}: ${c.parsed} produktów` }
                    }
                }
            }
        });
    }

    // --- ECharts: Kraje — POZIOME SŁUPKI (horizontal bar) z flagami na osi Y ---
    function renderCountriesChart(data) {
        destroyChart();
        chartType = 'echarts';

        const container = document.querySelector('.side-chart-box');
        container.innerHTML = '<div id="mainChartEC" style="width:100%;height:100%;"></div>';

        const counts = {};
        data.forEach(p => {
            (p.allOffers || []).forEach(o => {
                if (o.isMe) return;
                const name = o.regionName || 'Brak';
                counts[name] = (counts[name] || 0) + 1;
            });
        });

        // Sortuj malejąco po ilości, odwróć do wyświetlenia (najwyższy na górze)
        const sorted = Object.entries(counts).sort((a, b) => a[1] - b[1]); // rosnąco = na dole najmniej

        if (sorted.length === 0) {
            container.innerHTML = '<div style="display:flex;align-items:center;justify-content:center;height:100%;color:#888;font-size:12px;">Brak danych krajów</div>';
            return;
        }

        const labels = sorted.map(s => s[0]);
        const values = sorted.map(s => s[1]);
        const minVal = Math.min(...values);
        const maxVal = Math.max(...values);

        // Flagi na osi Y — rich text
        const flagH = sorted.length <= 8 ? 16 : (sorted.length <= 14 ? 13 : 10);
        const flagW = Math.round(flagH * 1.5);

        const richConfig = {};
        labels.forEach((label, idx) => {
            richConfig['flag' + idx] = {
                height: flagH,
                width: flagW,
                backgroundColor: { image: '/images/' + label + '.png' }
            };
        });

        // Dynamiczna wysokość kontenera: min 160px, max ~pełna dostępna
        const dynamicHeight = Math.max(180, sorted.length * 28 + 40);
        const myEl = document.getElementById('mainChartEC');
        myEl.style.height = dynamicHeight + 'px';

        chartInstance = echarts.init(myEl);

        const labelFontSize = sorted.length <= 10 ? 10 : 9;

        chartInstance.setOption({
            grid: { left: flagW + 8, right: 6, top: 20, bottom: 4, containLabel: false },
            tooltip: {
                trigger: 'axis',
                axisPointer: { type: 'shadow' },
                backgroundColor: 'rgba(34, 34, 34, 0.95)',
                borderWidth: 0,
                textStyle: { color: '#fff', fontSize: 12 },
                formatter: function (params) {
                    const p = params[0];
                    return `<b>${p.name}</b><br/>${p.value} ofert`;
                }
            },
            yAxis: {
                type: 'category',
                data: labels,
                axisTick: { show: false },
                axisLine: { show: false },
                axisLabel: {
                    interval: 0,
                    margin: 6,
                    formatter: (value) => '{flag' + labels.indexOf(value) + '|}',
                    rich: richConfig
                }
            },
            xAxis: {
                type: 'value',
                show: false,
                splitLine: { show: false }
            },
            visualMap: {
                show: false,
                min: minVal,
                max: Math.max(maxVal, minVal + 1),
                inRange: {
                    color: ['rgba(0, 120, 130, 0.65)', 'rgba(0, 100, 110, 0.85)', 'rgba(0, 55, 66, 1)']
                }
            },
            series: [{
                type: 'bar',
                data: values,
                barCategoryGap: '30%',
                itemStyle: {
                    borderRadius: [0, 3, 3, 0]
                },
                emphasis: { disabled: true },
                label: {
                    show: true,
                    position: 'insideRight',
                    fontSize: labelFontSize,
                    color: '#fff',
                    fontWeight: 'bold',
                    distance: 4,
                    formatter: '{c}'
                },
                labelLayout: {
                    hideOverlap: false,
                    moveOverlap: 'shiftX'
                }
            }]
        });

        if (window._safariEChartsResizeObs) {
            try { window._safariEChartsResizeObs.disconnect(); } catch (e) { }
        }
        window._safariEChartsResizeObs = new ResizeObserver(() => {
            if (chartInstance && chartType === 'echarts') chartInstance.resize();
        });
        window._safariEChartsResizeObs.observe(myEl);
    }

    function updateColorCounts() {
        const all = { prIdeal: 0, prGray: 0, prToHigh: 0, prNoOffer: 0 };
        allProducts.forEach(p => { all[p.colorClass] = (all[p.colorClass] || 0) + 1; });

        const setLabel = (id, base, count) => {
            const lbl = document.querySelector(`label[for="${id}"]`);
            if (lbl) lbl.textContent = `${base} (${count})`;
        };
        setLabel('prNoOfferCheckbox', 'Brak Twojej oferty', all.prNoOffer);
        setLabel('prIdealCheckbox', 'Twoja oferta tańsza', all.prIdeal);
        setLabel('prGrayCheckbox', 'Cena podobna', all.prGray);
        setLabel('prToHighCheckbox', 'Twoja oferta droższa', all.prToHigh);
    }

    // ============================================================
    // FLAGS
    // ============================================================
    function updateFlagCounts(data) {
        const flagCounts = {};
        let noFlagCount = 0;
        data.forEach(p => {
            if (!p.flagIds || p.flagIds.length === 0) noFlagCount++;
            else p.flagIds.forEach(fid => { flagCounts[fid] = (flagCounts[fid] || 0) + 1; });
        });

        const container = document.getElementById('flagContainer');
        if (!container) return;
        container.innerHTML = '';

        const renderFlagRow = (fid, name, count) => {
            const incChecked = selectedFlagsInclude.has(String(fid)) ? 'checked' : '';
            const excChecked = selectedFlagsExclude.has(String(fid)) ? 'checked' : '';
            return `
                <div class="flag-filter-group">
                    <div class="form-check form-check-inline check-include" style="margin-right:0;">
                        <input class="form-check-input flagFilterInclude" type="checkbox"
                               id="flagInc_${fid}" value="${fid}" ${incChecked}
                               title="Pokaż tylko z tą flagą">
                    </div>
                    <div class="form-check form-check-inline check-exclude" style="margin-right:0; padding-left:16px;">
                        <input class="form-check-input flagFilterExclude" type="checkbox"
                               id="flagExc_${fid}" value="${fid}" ${excChecked}
                               title="Ukryj produkty z tą flagą">
                    </div>
                    <span style="font-size:14px; font-weight:400;">${name} (${count})</span>
                </div>`;
        };

        flags.forEach(flag => {
            container.insertAdjacentHTML('beforeend',
                renderFlagRow(flag.flagId, flag.flagName, flagCounts[flag.flagId] || 0));
        });
        container.insertAdjacentHTML('beforeend', renderFlagRow('noFlag', 'Brak flagi', noFlagCount));

        container.querySelectorAll('.flagFilterInclude, .flagFilterExclude').forEach(cb => {
            cb.addEventListener('change', function () {
                const val = this.value;
                const isInc = this.classList.contains('flagFilterInclude');
                if (isInc) {
                    if (this.checked) {
                        const exc = document.getElementById(`flagExc_${val}`);
                        if (exc) { exc.checked = false; selectedFlagsExclude.delete(val); }
                        selectedFlagsInclude.add(val);
                    } else selectedFlagsInclude.delete(val);
                } else {
                    if (this.checked) {
                        const inc = document.getElementById(`flagInc_${val}`);
                        if (inc) { inc.checked = false; selectedFlagsInclude.delete(val); }
                        selectedFlagsExclude.add(val);
                    } else selectedFlagsExclude.delete(val);
                }
                filterPricesAndUpdateUI();
            });
        });
    }

    // ============================================================
    // PAGINATION
    // ============================================================
    function renderPaginationControls(totalItems) {
        const totalPages = Math.ceil(totalItems / itemsPerPage);
        const container = document.getElementById('paginationContainer');
        container.innerHTML = '';
        if (totalPages <= 1) return;

        const mkBtn = (html, disabled, onclick, isActive) => {
            const b = document.createElement('button');
            b.innerHTML = html;
            if (disabled) b.disabled = true;
            if (isActive) b.classList.add('active');
            b.addEventListener('click', onclick);
            return b;
        };

        container.appendChild(mkBtn('<i class="fa fa-chevron-circle-left"></i>',
            currentPage === 1, () => { if (currentPage > 1) { currentPage--; filterPricesAndUpdateUI(false); } }));

        const maxVisible = 5;
        let start = Math.max(1, currentPage - Math.floor(maxVisible / 2));
        let end = start + maxVisible - 1;
        if (end > totalPages) { end = totalPages; start = Math.max(1, end - maxVisible + 1); }

        if (start > 1) {
            container.appendChild(mkBtn('1', false, () => { currentPage = 1; filterPricesAndUpdateUI(false); }));
            if (start > 2) {
                const dots = document.createElement('span');
                dots.innerHTML = '&hellip;'; dots.style.margin = '0 5px';
                container.appendChild(dots);
            }
        }

        for (let i = start; i <= end; i++) {
            container.appendChild(mkBtn(String(i), false,
                () => { currentPage = i; filterPricesAndUpdateUI(false); }, i === currentPage));
        }

        if (end < totalPages) {
            if (end < totalPages - 1) {
                const dots = document.createElement('span');
                dots.innerHTML = '&hellip;'; dots.style.margin = '0 5px';
                container.appendChild(dots);
            }
            container.appendChild(mkBtn(String(totalPages), false,
                () => { currentPage = totalPages; filterPricesAndUpdateUI(false); }));
        }

        container.appendChild(mkBtn('<i class="fa fa-chevron-circle-right"></i>',
            currentPage === totalPages,
            () => { if (currentPage < totalPages) { currentPage++; filterPricesAndUpdateUI(false); } }));
    }

    // ============================================================
    // RENDER PRODUCT CARDS
    // ============================================================
    function getIdentifierForProduct(p) {
        switch (identifierForSimulation) {
            case 'ID': return { label: 'ID', value: p.externalId ? String(p.externalId) : null };
            case 'ProducerCode': return { label: 'SKU', value: p.producerCode || null };
            default: return { label: 'EAN', value: p.ean || null };
        }
    }

    function buildStatsBlocks(p) {
        const blocks = [];

        blocks.push(`
            <div class="price-box-column-offers-a">
                <span class="data-channel"><i class="fa-solid fa-globe" style="color:grey;font-size:14px;"></i></span>
                <div class="offer-count-box"><p>${p.offerCount || 0} ${pluralOferta(p.offerCount || 0)}</p></div>
            </div>`);

        blocks.push(`
            <div class="price-box-column-offers-a">
                <span class="data-channel"><i class="fas fa-trophy" style="color:grey;font-size:14px;"></i></span>
                <div class="offer-count-box"><p>${p.myPosition || 'N/A'}</p></div>
            </div>`);

        if (p.producer) {
            blocks.push(`
                <div class="price-box-column-offers-a">
                    <span class="data-channel"><i class="fa-solid fa-tag" style="color:grey;font-size:14px;"></i></span>
                    <div class="offer-count-box"><p>${p.producer}</p></div>
                </div>`);
        }

        if (p.countriesCount && p.countriesCount > 0) {
            blocks.push(`
                <div class="price-box-column-offers-a" title="Liczba krajów z ofertami konkurencji">
                    <span class="data-channel"><i class="fa-solid fa-earth-europe" style="color:grey;font-size:14px;"></i></span>
                    <div class="offer-count-box"><p>${p.countriesCount} ${pluralKraj(p.countriesCount)}</p></div>
                </div>`);
        }

        return blocks.join('');
    }

    // ============================================================
    // BOKS KONKURENTA — z dodanymi statystykami ofert
    // ============================================================
    function buildCompetitorBox(p, storeSearchTerm) {
        const stats = computeOfferStats(p);

        if (!p.bestCalculatedPrice) {
            // Brak konkurencji — ale pokażmy ile ofert droższych jeśli my mamy cenę
            let statsHtml = '';
            if (p.myCalculatedPrice && stats.totalCompetitors === 0) {
                statsHtml = `
                    <div class="safari-offer-stats-line">
                        <i class="fa-solid fa-shield-halved" style="color:#198754;font-size:11px;"></i>
                        <span>Brak konkurencji w zakresie</span>
                    </div>`;
            }
            return `
                <div class="price-box-column">
                    <div class="price-box-column-text">
                        <span style="font-weight:500;font-size:17px;">Brak konkurencji</span>
                        <div style="color:#444;">w wybranym zakresie</div>
                        ${statsHtml}
                    </div>
                    <div class="price-box-column-text">
                        <span class="Position" style="background-color:#9C9C9C;">Brak ofert</span>
                    </div>
                </div>`;
        }

        const compStoreHL = highlightMatches(p.bestStoreName || '', storeSearchTerm);
        const flagImg = p.bestRegionName
            ? `<img src="/images/${p.bestRegionName}.png" class="safari-country-flag" onerror="this.style.display='none'"/>`
            : '';

        const showOriginal = p.bestOriginalPrice
            && p.bestCurrency
            && p.bestCurrency !== 'PLN'
            && parseFloat(p.bestOriginalPrice) !== parseFloat(p.bestCalculatedPrice);

        // Statystyki ofert w boksie konkurenta
        let offerStatsHtml = '<div class="safari-offer-stats-wrap">';

        // Ile ofert tańszych od naszej ceny
        if (p.myCalculatedPrice && p.myCalculatedPrice > 0) {
            if (stats.cheaperThanMe > 0) {
                offerStatsHtml += `
                    <div class="safari-offer-stats-line safari-stat-bad">
                        <i class="fa-solid fa-arrow-down" style="font-size:10px;"></i>
                        <span>${stats.cheaperThanMe} ${pluralOferta(stats.cheaperThanMe)} tańszych od Ciebie</span>
                    </div>`;
            }
            if (stats.moreExpensiveThanMe > 0) {
                offerStatsHtml += `
                    <div class="safari-offer-stats-line safari-stat-good">
                        <i class="fa-solid fa-arrow-up" style="font-size:10px;"></i>
                        <span>${stats.moreExpensiveThanMe} ${pluralOferta(stats.moreExpensiveThanMe)} droższych od Ciebie</span>
                    </div>`;
            }
        }

        // Ile ofert poniżej ceny zakupu
        if (p.marginPrice && p.marginPrice > 0) {
            if (stats.belowPurchase > 0) {
                offerStatsHtml += `
                    <div class="safari-offer-stats-line safari-stat-alarm">
                        <i class="fa-solid fa-triangle-exclamation" style="font-size:10px;"></i>
                        <span>${stats.belowPurchase} ${pluralOferta(stats.belowPurchase)} poniżej ceny zakupu</span>
                    </div>`;
            }
        }

        offerStatsHtml += '</div>';

        const origCurrencyInline = showOriginal
            ? ` <span style="color:#888;font-size:12px;margin-left:4px;">| Org: ${formatOriginalPrice(p.bestOriginalPrice, p.bestCurrency)}</span>`
            : '';

        return `
            <div class="price-box-column">
                <div class="price-box-column-text">
                    <div style="display:flex; align-items:center;">
                        <span style="font-weight:500; font-size:17px;">${formatPricePL(p.bestCalculatedPrice)}</span>
                    </div>
                    <div class="safari-store-country">${compStoreHL}</div>
                    <div class="safari-region-line">${flagImg}<span>${p.bestRegionName || ''}</span>${origCurrencyInline}</div>
                    ${offerStatsHtml}
                </div>
                <div class="price-box-column-text">
                    <span class="Position" style="background-color:#41C7C7;">Najtańsza konkurencja</span>
                </div>
            </div>`;
    }

    // ============================================================
    // BOKS MOJEJ CENY — z info o pozycji
    // ============================================================
    function buildMyPriceBox(p) {
        const stats = computeOfferStats(p);

        if (!p.myCalculatedPrice) {
            return `
                <div class="price-box-column">
                    <div class="price-box-column-text">
                        <span style="font-weight:500;font-size:17px;">Brak Twojej oferty</span>
                        <div style="color:#444;">${myStoreName}</div>
                    </div>
                    <div class="price-box-column-text">
                        <span class="Position" style="background-color:#9C9C9C;">Cena niedostępna</span>
                    </div>
                </div>`;
        }

        const flagImg = `<img src="/images/${p.myRegionName || 'Polska'}.png" class="safari-country-flag" onerror="this.style.display='none'"/>`;

        let marginHtml = '';
        if (p.marginPrice != null) {
            const mAmount = p.marginAmount;
            const mPerc = p.marginPercentage || 0;
            const positive = mAmount >= 0;
            const bgClass = positive ? 'price-box-diff-margin-ib-positive' : 'price-box-diff-margin-ib-negative';
            const badgeClass = positive ? 'price-badge-positive' : 'price-badge-negative';
            const sign = positive ? '+' : '';

            marginHtml = `
                <div class="price-box-diff-margin-ib ${bgClass}" style="margin-top:4px;">
                    <span class="price-badge price-badge-neutral">Cena zakupu</span>
                    <p>${formatPricePL(p.marginPrice)}</p>
                </div>
                <div class="price-box-diff-margin-ib ${bgClass}" style="margin-top:3px;">
                    <span class="price-badge ${badgeClass}">Zysk (narzut)</span>
                    <p>${sign}${formatPricePL(Math.abs(mAmount), false)} PLN
                       (${sign}${Math.abs(mPerc).toFixed(2)}%)</p>
                </div>`;
        }

        let indexBadge = '';
        if (p.percentageDifference != null) {
            const v = p.percentageDifference;
            let cls = 'index-gray';
            if (v > setSafariPrice2) cls = 'index-red';
            else if (v < -setSafariPrice1) cls = 'index-blue';
            const sign = v > 0 ? '+' : '';
            indexBadge = `<span class="market-index-badge ${cls}">Twoja cena: ${sign}${v.toFixed(2)}%</span>`;
        }

        // Gdy jesteśmy najtańsi — pokaż ile droższych
        let positionStatsHtml = '';
        if (stats.moreExpensiveThanMe > 0 && stats.cheaperThanMe === 0) {
            positionStatsHtml = `
                <div class="safari-offer-stats-wrap" style="margin-top:6px;">
                    <div class="safari-offer-stats-line safari-stat-good">
                        <i class="fa-solid fa-crown" style="font-size:10px;"></i>
                        <span>Najtańsza oferta! ${stats.moreExpensiveThanMe} droższych</span>
                    </div>
                </div>`;
        }

        return `
            <div class="price-box-column">
                <div class="price-box-column-text">
                    <div style="display:flex; align-items:center;">
                        <span style="font-weight:500; font-size:17px;">${formatPricePL(p.myCalculatedPrice)}</span>
                    </div>
                    <div class="safari-store-country">${myStoreName}</div>
                    <div class="safari-region-line">${flagImg}<span>${p.myRegionName || 'Polska'}</span></div>
                    ${marginHtml}
                    ${positionStatsHtml}
                </div>
                <div class="price-box-column-text">${indexBadge}</div>
            </div>`;
    }

    // ============================================================
    // BOKS RÓŻNIC — PERSPEKTYWA KONKURENTA
    //
    // Wiersz 1 (Konkurent vs nasza cena):
    //   priceDifference = nasza cena - cena konkurenta (z backendu)
    //   > 0 = konkurent TAŃSZY od nas → "Konkurent tańszy o X PLN" ▼ czerwony
    //   < 0 = konkurent DROŻSZY od nas → "Konkurent droższy o X PLN" ▲ zielony
    //
    // Wiersz 2 (Konkurent vs cena zakupu):
    //   bestVsPurchaseDiff = cena konkurenta - cena zakupu (z backendu)
    //   < 0 = konkurent TAŃSZY od ceny zakupu → ALARM ▼ czerwony
    //   > 0 = konkurent DROŻSZY od ceny zakupu → OK ▲ szary
    //
    // Zawsze pokazujemy WARTOŚĆ BEZWZGLĘDNĄ z opisem kierunku.
    // ============================================================
    function buildDiffBox(p) {
        // ----- Wiersz 1: konkurent vs nasza cena -----
        let row1 = '';
        if (p.priceDifference == null) {
            row1 = `<div class="price-box-column-text">
                        <span style="color:#888;">Brak danych do porównania</span>
                    </div>`;
        } else {
            const diff = p.priceDifference; // myPrice - competitorPrice
            const diffPerc = p.percentageDifference || 0;
            const absDiff = Math.abs(diff);
            const absDiffPerc = Math.abs(diffPerc);

            let arrowIcon, arrowColor, badgeText, diffColorClass, description;

            if (diff > 0) {
                // Konkurent tańszy od nas
                arrowIcon = 'fa-arrow-down';
                arrowColor = 'rgba(171, 37, 32, 0.85)';
                badgeText = 'Konkurent tańszy';
                diffColorClass = 'safari-diff-row-bad';
                description = 'Konkurent tańszy o';
            } else if (diff < 0) {
                // Konkurent droższy od nas
                arrowIcon = 'fa-arrow-up';
                arrowColor = 'rgba(0, 145, 123, 0.85)';
                badgeText = 'Konkurent droższy';
                diffColorClass = 'safari-diff-row-good';
                description = 'Konkurent droższy o';
            } else {
                arrowIcon = 'fa-equals';
                arrowColor = 'rgba(40, 40, 40, 0.7)';
                badgeText = 'Cena równa';
                diffColorClass = 'safari-diff-row-neutral';
                description = 'Cena równa';
            }

            row1 = `
                <div class="price-box-column-text">
                    <div class="safari-diff-value-row ${diffColorClass}">
                        <i class="fa-solid ${arrowIcon}" style="color:${arrowColor};font-size:14px;margin-right:6px;"></i>
                        <span style="font-weight:500; font-size:17px;">${formatPricePL(absDiff, false)} PLN</span>
                    </div>
                    <div style="font-size:13px; color:#555; margin-top:2px;">${description}</div>
                    <div class="price-diff-stack" style="margin-top:4px;">
                        <div class="price-diff-stack-badge">${badgeText}</div>
                        <span class="diff-percentage small-font">${diff > 0 ? '-' : '+'}${absDiffPerc.toFixed(2)}%</span>
                    </div>
                </div>`;
        }

        // ----- Wiersz 2: konkurent vs cena zakupu -----
        let row2 = '';
        if (p.bestVsPurchaseDiff != null) {
            const diff = p.bestVsPurchaseDiff; // competitorPrice - purchasePrice
            const diffPerc = p.bestVsPurchasePerc || 0;
            const absDiff = Math.abs(diff);
            const absDiffPerc = Math.abs(diffPerc);

            let arrowIcon, arrowColor, badgeText, diffColorClass, description;

            if (diff < 0) {
                // Konkurent tańszy od ceny zakupu — ALARM
                arrowIcon = 'fa-arrow-down';
                arrowColor = 'rgba(171, 37, 32, 0.85)';
                badgeText = 'Poniżej ceny zakupu!';
                diffColorClass = 'safari-diff-row-alarm';
                description = 'Konkurent tańszy od ceny zakupu o';
            } else if (diff > 0) {
                // Konkurent droższy od ceny zakupu — OK
                arrowIcon = 'fa-arrow-up';
                arrowColor = 'rgba(40, 40, 40, 0.6)';
                badgeText = 'Powyżej ceny zakupu';
                diffColorClass = 'safari-diff-row-neutral';
                description = 'Konkurent droższy od ceny zakupu o';
            } else {
                arrowIcon = 'fa-equals';
                arrowColor = 'rgba(40, 40, 40, 0.6)';
                badgeText = 'Równa cenie zakupu';
                diffColorClass = 'safari-diff-row-neutral';
                description = 'Cena równa cenie zakupu';
            }

            row2 = `
                <div class="safari-diff-separator"></div>
                <div class="price-box-column-text">
                    <div class="safari-diff-value-row ${diffColorClass}">
                        <i class="fa-solid ${arrowIcon}" style="color:${arrowColor};font-size:14px;margin-right:6px;"></i>
                        <span style="font-weight:500; font-size:17px;">${formatPricePL(absDiff, false)} PLN</span>
                    </div>
                    <div style="font-size:13px; color:#555; margin-top:2px;">${description}</div>
                    <div class="price-diff-stack" style="margin-top:4px;">
                        <div class="price-diff-stack-badge">${badgeText}</div>
                        <span class="diff-percentage small-font">${diff < 0 ? '-' : '+'}${absDiffPerc.toFixed(2)}%</span>
                    </div>
                </div>`;
        } else if (p.marginPrice == null && p.bestCalculatedPrice) {
            row2 = `
                <div class="safari-diff-separator"></div>
                <div class="price-box-column-text">
                    <span style="color:#888; font-size:12px;">Brak ceny zakupu w produkcie</span>
                </div>`;
        }

        return `<div class="price-box-column">${row1}${row2}</div>`;
    }

    function renderPrices(data) {
        const container = document.getElementById('priceContainer');
        const productSearchTerm = document.getElementById('productSearch').value.trim();
        const storeSearchTerm = document.getElementById('storeSearch').value.trim();
        container.innerHTML = '';

        const startIdx = (currentPage - 1) * itemsPerPage;
        const paged = data.slice(startIdx, currentPage * itemsPerPage);

        const fragment = document.createDocumentFragment();

        paged.forEach(p => {
            const detailsUrl = `/Safari/ProductPriceDetails?reportId=${reportId}&productId=${p.productId}` +
                (regionId ? `&regionId=${regionId}` : '');

            const box = document.createElement('div');
            box.className = 'price-box ' + p.colorClass;
            box.dataset.productId = p.productId;
            box.addEventListener('click', () => window.open(detailsUrl, '_blank'));

            // Header
            const space = document.createElement('div');
            space.className = 'price-box-space';

            const left = document.createElement('div');
            left.className = 'price-box-left-column';

            const nameDiv = document.createElement('div');
            nameDiv.className = 'price-box-column-name';
            nameDiv.innerHTML = highlightMatches(p.productName || '', productSearchTerm);

            const flagsCont = document.createElement('div');
            flagsCont.className = 'flags-container';
            (p.flagIds || []).forEach(fid => {
                const fl = flags.find(f => f.flagId === fid);
                if (!fl) return;
                const fs = document.createElement('span');
                fs.className = 'flag';
                fs.style.color = fl.flagColor;
                fs.style.border = '2px solid ' + fl.flagColor;
                fs.style.backgroundColor = hexToRgba(fl.flagColor, 0.3);
                fs.textContent = fl.flagName;
                flagsCont.appendChild(fs);
            });

            left.appendChild(nameDiv);
            left.appendChild(flagsCont);

            const right = document.createElement('div');
            right.className = 'price-box-right-column';

            const apiBox = document.createElement('span');
            apiBox.className = 'ApiBox';
            const ident = getIdentifierForProduct(p);
            if (ident.value) {
                apiBox.innerHTML = `${ident.label} ${highlightMatches(ident.value, productSearchTerm, 'highlighted-text-yellow')}`;
                apiBox.title = 'Kliknij, aby skopiować';
                apiBox.addEventListener('click', e => {
                    e.stopPropagation();
                    navigator.clipboard.writeText(ident.value).then(() => {
                        const oh = apiBox.innerHTML;
                        apiBox.innerHTML = 'Skopiowano!';
                        apiBox.style.backgroundColor = '#198754';
                        setTimeout(() => { apiBox.innerHTML = oh; apiBox.style.backgroundColor = ''; }, 1500);
                    });
                });
            } else {
                apiBox.innerHTML = `Brak ${ident.label}`;
            }
            right.appendChild(apiBox);

            space.appendChild(left);
            space.appendChild(right);

            // Data row
            const dataRow = document.createElement('div');
            dataRow.className = 'price-box-data';

            const colorBar = document.createElement('div');
            colorBar.className = 'color-bar';
            dataRow.appendChild(colorBar);

            const img = document.createElement('img');
            img.dataset.src = p.mainUrl || '/images/no-image.png';
            img.alt = p.productName || '';
            img.className = 'lazy-load';
            img.style.cssText = 'width:142px;height:182px;object-fit:contain;background:#fff;border:1px solid #e3e3e3;border-radius:4px;padding:8px;margin:0 3px;';
            dataRow.appendChild(img);

            const stats = document.createElement('div');
            stats.className = 'price-box-stats-container';
            stats.innerHTML = buildStatsBlocks(p);
            dataRow.appendChild(stats);

            dataRow.insertAdjacentHTML('beforeend', buildCompetitorBox(p, storeSearchTerm));
            dataRow.insertAdjacentHTML('beforeend', buildMyPriceBox(p));
            dataRow.insertAdjacentHTML('beforeend', buildDiffBox(p));

            box.appendChild(space);
            box.appendChild(dataRow);
            fragment.appendChild(box);
        });

        container.appendChild(fragment);
        renderPaginationControls(data.length);

        // Lazy load images
        const lazyImages = container.querySelectorAll('.lazy-load');
        const observer = new IntersectionObserver((entries, obs) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    const imgEl = entry.target;
                    imgEl.src = imgEl.dataset.src;
                    imgEl.onload = () => imgEl.classList.add('loaded');
                    imgEl.onerror = () => { imgEl.src = '/images/no-image.png'; };
                    obs.unobserve(imgEl);
                }
            });
        }, { rootMargin: '100px' });
        lazyImages.forEach(i => observer.observe(i));
    }

    // ============================================================
    // PRODUCERS
    // ============================================================
    function populateProducerFilter() {
        const dd = document.getElementById('producerFilterDropdown');
        const counts = allProducts.reduce((m, p) => {
            if (p.producer) m[p.producer] = (m[p.producer] || 0) + 1;
            return m;
        }, {});
        const producers = Object.keys(counts).sort((a, b) => a.localeCompare(b));
        dd.innerHTML = '<option value="">Wszystkie marki</option>';
        producers.forEach(prod => {
            const opt = document.createElement('option');
            opt.value = prod;
            opt.textContent = `${prod} (${counts[prod]})`;
            dd.appendChild(opt);
        });
    }

    // ============================================================
    // LOAD DATA
    // ============================================================
    function loadData() {
        showLoading();
        const url = `/Safari/GetSafariReportData?reportId=${reportId}` + (regionId ? `&regionId=${regionId}` : '');
        fetch(url)
            .then(r => r.json())
            .then(resp => {
                if (resp.error) {
                    showGlobalNotification(`<p>Błąd: ${resp.error}</p>`);
                    return;
                }
                allProducts = resp.products || [];
                myStoreName = resp.myStoreName || myStoreName;
                setSafariPrice1 = parseFloat(resp.setSafariPrice1) || setSafariPrice1;
                setSafariPrice2 = parseFloat(resp.setSafariPrice2) || setSafariPrice2;
                usePriceDifference = !!resp.usePriceDiffSafari;
                identifierForSimulation = resp.identifierForSimulation || 'EAN';

                document.getElementById('threshold1').value = setSafariPrice1;
                document.getElementById('threshold2').value = setSafariPrice2;
                document.getElementById('usePriceDifference').checked = usePriceDifference;
                document.getElementById('identifierSelect').value = identifierForSimulation;
                updateUnits();

                recomputeColorClasses();
                updateSlidersRanges();
                populateProducerFilter();

                filterPricesAndUpdateUI();
            })
            .catch(err => {
                console.error(err);
                showGlobalNotification('<p>Błąd ładowania danych raportu</p>');
            })
            .finally(hideLoading);
    }

    function updateUnits() {
        const unit = usePriceDifference ? 'PLN' : '%';
        document.getElementById('unitLabel1').textContent = unit;
        document.getElementById('unitLabel2').textContent = unit;
    }

    // ============================================================
    // SAVE THRESHOLDS
    // ============================================================
    document.getElementById('savePriceValues').addEventListener('click', function () {
        const t1 = parseFloat(document.getElementById('threshold1').value);
        const t2 = parseFloat(document.getElementById('threshold2').value);
        const upd = document.getElementById('usePriceDifference').checked;
        const sid = parseInt(this.getAttribute('data-store-id'));

        fetch('/Safari/SaveSafariPriceValues', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                StoreId: sid,
                SetSafariPrice1: t1,
                SetSafariPrice2: t2,
                UsePriceDiffSafari: upd
            })
        })
            .then(r => r.json())
            .then(resp => {
                if (resp.success) {
                    setSafariPrice1 = t1;
                    setSafariPrice2 = t2;
                    usePriceDifference = upd;
                    updateUnits();
                    showGlobalUpdate('<p style="font-weight:bold;">Progi zapisane</p>');
                    filterPricesAndUpdateUI();
                } else {
                    showGlobalNotification(`<p>Błąd: ${resp.message || 'nieznany'}</p>`);
                }
            })
            .catch(err => {
                console.error(err);
                showGlobalNotification('<p>Błąd zapisu</p>');
            });
    });

    // ============================================================
    // SORT BUTTONS
    // ============================================================
    function setupSortButton(id, label) {
        const btn = document.getElementById(id);
        if (!btn) return;
        btn.setAttribute('data-base-label', label);

        btn.addEventListener('click', function () {
            const cur = sortingState[id];
            Object.keys(sortingState).forEach(k => {
                if (k !== id) {
                    sortingState[k] = null;
                    const b = document.getElementById(k);
                    if (b) {
                        b.classList.remove('active');
                        b.textContent = b.getAttribute('data-base-label') || b.textContent.replace(/ ↑| ↓/g, '');
                    }
                }
            });
            if (cur === null) {
                sortingState[id] = 'asc'; this.textContent = label + ' ↑'; this.classList.add('active');
            } else if (cur === 'asc') {
                sortingState[id] = 'desc'; this.textContent = label + ' ↓';
            } else {
                sortingState[id] = null; this.textContent = label; this.classList.remove('active');
            }
            filterPricesAndUpdateUI();
        });
    }

    setupSortButton('sortName', 'A-Z');
    setupSortButton('sortPrice', 'Cena');
    setupSortButton('sortDifferenceAmount', 'Różnica PLN');
    setupSortButton('sortDifferencePercent', 'Różnica %');
    setupSortButton('sortMarginAmount', 'Narzut PLN');
    setupSortButton('sortMarginPercent', 'Narzut %');
    setupSortButton('sortOfferCount', 'Ilość ofert');

    // ============================================================
    // EVENTS
    // ============================================================
    const debouncedFilter = debounce(filterPricesAndUpdateUI, 300);

    document.getElementById('productSearch').addEventListener('input', debouncedFilter);
    document.getElementById('storeSearch').addEventListener('input', debouncedFilter);
    document.getElementById('producerFilterDropdown').addEventListener('change', filterPricesAndUpdateUI);

    document.getElementById('identifierSelect').addEventListener('change', function () {
        identifierForSimulation = this.value;
        filterPricesAndUpdateUI();
    });

    document.getElementById('regionFilter').addEventListener('change', function () {
        const v = this.value;
        const url = new URL(window.location.href);
        if (v) url.searchParams.set('regionId', v);
        else url.searchParams.delete('regionId');
        window.location.href = url.toString();
    });

    document.getElementById('threshold1').addEventListener('change', function () {
        setSafariPrice1 = parseFloat(this.value) || 0;
        filterPricesAndUpdateUI();
    });
    document.getElementById('threshold2').addEventListener('change', function () {
        setSafariPrice2 = parseFloat(this.value) || 0;
        filterPricesAndUpdateUI();
    });
    document.getElementById('usePriceDifference').addEventListener('change', function () {
        usePriceDifference = this.checked;
        updateUnits();
        filterPricesAndUpdateUI();
    });

    document.querySelectorAll('.colorFilter').forEach(cb => {
        cb.addEventListener('change', function () {
            if (this.checked) selectedColors.add(this.value);
            else selectedColors.delete(this.value);
            filterPricesAndUpdateUI();
        });
    });

    document.querySelectorAll('.chart-tab').forEach(tab => {
        tab.addEventListener('click', function () {
            document.querySelectorAll('.chart-tab').forEach(t => t.classList.remove('active'));
            this.classList.add('active');
            currentChartType = this.getAttribute('data-chart');
            renderCurrentChart(currentlyFilteredProducts);
        });
    });

    document.getElementById('exportSafariExcelBtn').addEventListener('click', function () {
        let url = `/Safari/ExportToExcel?reportId=${reportId}`;
        if (regionId) url += `&regionId=${regionId}`;
        window.location.href = url;
    });

    // ============================================================
    // INIT
    // ============================================================
    initSliders();
    updateUnits();
    loadData();
});