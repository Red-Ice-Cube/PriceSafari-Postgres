document.addEventListener("DOMContentLoaded", function () {

    const clearChangesButton = document.getElementById("clearChangesButton");

    function formatPricePL(value, includeUnit = true) {
        if (value === null || value === undefined || isNaN(parseFloat(value))) {
            return "-";
        }
        const numberValue = parseFloat(value);
        const formatted = numberValue.toLocaleString('pl-PL', {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
        return includeUnit ? formatted + ' PLN' : formatted;
    }

    function getColorForScore(score) {
        const hue = (score / 100) * 120;
        return `hsl(${hue}, 70%, 45%)`;
    }

    function createDonutChart(score, iconPath) {
        const color = getColorForScore(score);
        const percentage = Math.max(0, Math.min(100, score));
        const roundedScore = Math.round(score);

        return `
        <div style="display: inline-block; margin: 8px 8px 0 0; text-align: center; vertical-align: middle;">
            <div style="position: relative; display: inline-block; width: 80px; height: 80px;">
                <div style="
                    width: 80px;
                    height: 80px;
                    border-radius: 50%;
                    border: 1px solid #e3e3e3;
                    background: conic-gradient(${color} 0% ${percentage}%, #f0f0f0 ${percentage}% 100%); 
                "></div>
                <div style="
                    position: absolute;
                    top: 10px;
                    left: 10px; 
                    width: 60px; 
                    height: 60px; 
                    background: #fff;
                    border: 1px solid #e3e3e3;
                    border-radius: 50%;
                "></div>
                <div style="
                    position: absolute;
                    top: 50%;
                    left: 50%;
                    transform: translate(-50%, -50%);
                    text-align: center;
                ">
                    <img src="${iconPath}" alt="icon" style="width:16px; height:16px; vertical-align: middle; margin-bottom: 2px;" />
                    <div style="
                        font-size: 16px;
                        font-weight: 400; 
                        color: #222222;
                    ">
                        ${roundedScore}
                    </div>
                </div>
            </div>
        </div>
        `;
    }

    const localStorageKey = `selectedAllegroPriceChanges_${storeId}`;
    var selectedPriceChanges = [];
    var sessionScrapId = null;

    function loadChangesFromStorage() {
        const storedDataJSON = localStorage.getItem(localStorageKey);
        if (storedDataJSON) {
            try {
                const storedData = JSON.parse(storedDataJSON);
                if (storedData && storedData.scrapId && Array.isArray(storedData.changes)) {
                    selectedPriceChanges = storedData.changes;
                    sessionScrapId = storedData.scrapId;
                } else {
                    localStorage.removeItem(localStorageKey);
                }
            } catch (err) {
                console.error("Błąd parsowania storedChanges z Local Storage:", err);
                localStorage.removeItem(localStorageKey);
            }
        }
    }

    let currentOurStoreName = storeId || "Sklep";
    let globalLatestScrapId = null;
    let usePriceWithDeliverySetting = false;
    let globalIncludeCommissionSetting = false;
    let originalRowsData = [];

    function updatePriceChangeSummary(forceClear = false) {
        if (forceClear) {
            selectedPriceChanges = [];
        }
        var increasedCount = selectedPriceChanges.filter(item => parseFloat(item.newPrice) > parseFloat(item.currentPrice)).length;
        var decreasedCount = selectedPriceChanges.filter(item => parseFloat(item.newPrice) < parseFloat(item.currentPrice)).length;
        var totalCount = selectedPriceChanges.length;

        var summaryBar = document.getElementById("priceChangeSummary");
        var summaryText = document.getElementById("summaryText");
        if (summaryBar && summaryText) {
            summaryText.innerHTML =
                `<div class='price-change-up' style='display: inline-block;'><span style='color: red;'>▲</span> ${increasedCount}</div>` +
                `<div class='price-change-down' style='display: inline-block;'><span style='color: green;'>▼</span> ${decreasedCount}</div>`;
            summaryBar.style.display = 'flex';
        }
        var simulateButton = document.getElementById("simulateButton");
        if (simulateButton) {

            simulateButton.disabled = false;
            simulateButton.style.opacity = '1';
            simulateButton.style.cursor = 'pointer';
        }

        const simCounter = document.getElementById('simulationTabCounter');
        if (simCounter) {
            simCounter.textContent = totalCount;
        }
    }

    function savePriceChanges() {
        const dataToStore = {
            scrapId: sessionScrapId,
            changes: selectedPriceChanges
        };
        localStorage.setItem(localStorageKey, JSON.stringify(dataToStore));
    }

    function clearPriceChanges() {
        selectedPriceChanges = [];
        sessionScrapId = null;
        savePriceChanges();
        updatePriceChangeSummary();

        const tableContainer = document.getElementById("simulationModalBody");
        if (tableContainer) {
            tableContainer.innerHTML = '<p style="text-align: center; padding: 20px; font-size: 16px;">Brak produktów do symulacji.</p>';
        }
        const simulationTable = document.getElementById("simulationTable");
        if (simulationTable) simulationTable.remove();
        originalRowsData = [];
        if (typeof window.refreshPriceBoxStates === 'function') {
            window.refreshPriceBoxStates();
        }
    }

    document.addEventListener('simulationDataCleared', function () {
        selectedPriceChanges = [];
        sessionScrapId = null;
        updatePriceChangeSummary();
    });

    document.addEventListener('priceBoxChange', function (event) {
        const { productId, myIdAllegro, productName, currentPrice, newPrice, scrapId, stepPriceApplied, stepUnitApplied } = event.detail;
        if (selectedPriceChanges.length === 0) {
            sessionScrapId = scrapId;
        } else if (sessionScrapId !== scrapId) {
            console.warn(`ScrapId w sesji (${sessionScrapId}) różni się od scrapId eventu (${scrapId}). Czyszczenie starych zmian.`);
            selectedPriceChanges = [];
            sessionScrapId = scrapId;
        }
        var existingIndex = selectedPriceChanges.findIndex(item => String(item.productId) === String(productId));
        const changeData = {
            productId: String(productId),
            myIdAllegro: myIdAllegro,
            productName,
            currentPrice: parseFloat(currentPrice),
            newPrice: parseFloat(newPrice),
            stepPriceApplied: parseFloat(stepPriceApplied),
            stepUnitApplied
        };
        if (existingIndex > -1) {
            selectedPriceChanges[existingIndex] = changeData;
        } else {
            selectedPriceChanges.push(changeData);
        }
        savePriceChanges();
        updatePriceChangeSummary();
    });

    document.addEventListener('priceBoxChangeRemove', function (event) {
        const { productId } = event.detail;
        const productIdStr = String(productId);
        selectedPriceChanges = selectedPriceChanges.filter(item => item.productId !== productIdStr);
        if (selectedPriceChanges.length === 0) {
            sessionScrapId = null;
        }
        savePriceChanges();
        updatePriceChangeSummary();

        const rowElement = document.querySelector(`#simulationTbody tr button[data-product-id='${productIdStr}']`)?.closest('tr');
        if (rowElement) {
            rowElement.remove();
        }
        originalRowsData = originalRowsData.filter(rowItem => String(rowItem.productId) !== productIdStr);
        if (selectedPriceChanges.length === 0) {
            const tableContainer = document.getElementById("simulationModalBody");
            const simulationTable = document.getElementById("simulationTable");
            if (tableContainer && simulationTable) {
                simulationTable.remove();
                tableContainer.innerHTML = '<p style="text-align: center; padding: 20px; font-size: 16px;">Brak produktów do symulacji.</p>';
            }
        }
    });

    function showLoading() {
        const overlay = document.getElementById("loadingOverlay");
        if (overlay) overlay.style.display = "flex";
    }

    function hideLoading() {
        const overlay = document.getElementById("loadingOverlay");
        if (overlay) overlay.style.display = "none";
    }

    function parseRankingString(rankStr) {
        if (!rankStr || rankStr === "-") {
            return { rank: null, isRange: false, rangeSize: 1 };
        }
        const slashIndex = rankStr.indexOf("/");
        let relevantPart = (slashIndex !== -1) ? rankStr.substring(0, slashIndex).trim() : rankStr.trim();
        let isRange = false;
        let rangeSize = 1;
        let rankVal = null;
        if (relevantPart.includes("-")) {
            isRange = true;
            const parts = relevantPart.split("-");
            if (parts.length === 2) {
                const startVal = parseFloat(parts[0]);
                const endVal = parseFloat(parts[1]);
                if (!isNaN(startVal) && !isNaN(endVal)) {
                    rankVal = startVal;
                    rangeSize = (endVal - startVal) + 1;
                }
            }
        } else {
            const val = parseFloat(relevantPart);
            if (!isNaN(val)) {
                rankVal = val;
            }
        }
        return { rank: rankVal, isRange: isRange, rangeSize: rangeSize };
    }

    function computeOpportunityScore(item, simResult) {
        if (!simResult) {
            return { cost: 0, costFrac: 0, allegroGained: 0, allegroFinalScore: null, basePriceChangeType: 'unknown' };
        }
        const basePriceDiff = (simResult.baseNewPrice ?? 0) - (simResult.baseCurrentPrice ?? 0);
        let basePriceChangeType = 'none';
        if (basePriceDiff > 0.005) basePriceChangeType = 'increase';
        else if (basePriceDiff < -0.005) basePriceChangeType = 'decrease';
        let allegroOldData = parseRankingString(String(simResult.currentAllegroRanking));
        let allegroNewData = parseRankingString(String(simResult.newAllegroRanking));
        let totalA = simResult.totalAllegroOffers ? parseInt(simResult.totalAllegroOffers, 10) : 0;
        let effectivePriceDiff = item.newPrice - item.currentPrice;
        let cost = Math.abs(effectivePriceDiff);
        let costFrac = (item.currentPrice > 0) ? (cost / item.currentPrice) : 0;
        function getPlaceBonus(rankRounded) {
            if (rankRounded === 1) return 0.5;
            if (rankRounded === 2) return 0.2;
            if (rankRounded === 3) return 0.1;
            return 0;
        }
        function channelScore(oldRankData, newRankData, totalOffers) {
            if (oldRankData.rank === null || newRankData.rank === null || totalOffers < 1) return null;
            let effectiveOldRank = oldRankData.rank;
            let isOldRankRangeImprovement = false;
            if (oldRankData.isRange && oldRankData.rangeSize > 1) {
                effectiveOldRank = oldRankData.rank + oldRankData.rangeSize - 1;
                if (newRankData.rank < oldRankData.rank) {
                    isOldRankRangeImprovement = true;
                }
            }
            let gainedPos = effectiveOldRank - newRankData.rank;
            if (gainedPos <= 0 && !isOldRankRangeImprovement) return null;
            if (gainedPos <= 0 && isOldRankRangeImprovement) gainedPos = 0.5;
            let jumpFrac = gainedPos * Math.log10(totalOffers + 1);
            if (isOldRankRangeImprovement && gainedPos > 0) {
                jumpFrac *= 0.8;
            }
            let rankRounded = Math.round(newRankData.rank);
            let placeBonus = getPlaceBonus(rankRounded);
            if (newRankData.isRange && newRankData.rangeSize > 1) {
                placeBonus *= 0.4;
            }
            let effectiveCostFrac = (costFrac < 0.000001) ? 0.000001 : costFrac;
            let costFracAdjusted = Math.pow(effectiveCostFrac, 0.5);
            let offset = 0.0001;
            let raw = (jumpFrac + placeBonus) / (costFracAdjusted + offset);
            let val = Math.log10(raw + 1) * 35;
            val = Math.max(0, Math.min(100, val));
            return val;
        }
        const allegroFinalScore = channelScore(allegroOldData, allegroNewData, totalA);
        let calculatedAllegroGained = 0;
        if (allegroOldData.rank !== null && allegroNewData.rank !== null) {
            let effectiveOldAllegroRank = allegroOldData.rank;
            if (allegroOldData.isRange && allegroOldData.rangeSize > 1) {
                effectiveOldAllegroRank = allegroOldData.rank + allegroOldData.rangeSize - 1;
            }
            calculatedAllegroGained = effectiveOldAllegroRank - allegroNewData.rank;
        }
        return {
            cost,
            costFrac,
            allegroGained: calculatedAllegroGained > 0 ? calculatedAllegroGained : 0,
            allegroFinalScore,
            basePriceChangeType
        };
    }

    function buildPriceBlock(basePrice, marginPrice, apiAllegroCommission, allegroRank, allegroOffers, isConfirmedBlock = false) {
        const formattedBasePrice = formatPricePL(basePrice);
        let block = '<div class="price-info-box">';
        let headerText = isConfirmedBlock ? 'Cena wgrana' : 'Cena oferty';
        let headerStyle = isConfirmedBlock ? 'background: #dff0d8; border: 1px solid #c1e2b3;' : 'background: #f5f5f5; border: 1px solid #e3e3e3;';
        block += `<div class="price-info-item" style="padding: 4px 12px; ${headerStyle} border-radius: 5px; margin-bottom: 5px;">${headerText} | ${formattedBasePrice}</div>`;

        if (allegroRank && allegroRank !== "-") {
            block += `<div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px; margin-bottom: 5px;">Poz. cenowa | <img src="/images/AllegroIcon.png" alt="Allegro Icon" style="width:16px; height:16px; vertical-align: middle; margin-right: 3px;" /> ${allegroRank}</div>`;
        }

        if (apiAllegroCommission != null) {
            const formattedCommission = formatPricePL(apiAllegroCommission, false);
            const commissionStatusText = globalIncludeCommissionSetting ?
                '<span style="font-weight: 400; color: #555;"> | uwzględniona</span>' :
                '<span style="font-weight: 400; color: #999;"> | nieuwzględniona</span>';
            let commissionStyle = isConfirmedBlock ? 'background: #dff0d8; border: 1px solid #c1e2b3;' : 'background: #f5f5f5; border: 1px solid #e3e3e3;';
            block += `<div class="price-info-item" style="padding: 4px 12px; ${commissionStyle} border-radius: 5px; margin-bottom: 5px;">Prowizja | ${formattedCommission} PLN${commissionStatusText}</div>`;
        }
        let marginValue = null;
        let marginPercent = null;
        if (marginPrice != null && basePrice != null) {
            const commissionToDeduct = (globalIncludeCommissionSetting && apiAllegroCommission != null) ?
                parseFloat(apiAllegroCommission) : 0;
            const netPrice = parseFloat(basePrice) - commissionToDeduct;
            const purchasePrice = parseFloat(marginPrice);
            marginValue = netPrice - purchasePrice;
            marginPercent = (purchasePrice !== 0) ? (marginValue / purchasePrice) * 100 : null;
        }
        if (marginPercent != null && marginValue != null) {
            const formattedMarginValue = formatPricePL(marginValue);
            const formattedMarginPercent = parseFloat(marginPercent).toFixed(2);
            const sign = parseFloat(marginValue) >= 0 ? "+" : "";
            const cls = parseFloat(marginValue) >= 0 ? "priceBox-diff-margin" : "priceBox-diff-margin-minus";
            let marginBoxStyle = isConfirmedBlock ? 'background: #dff0d8;' : '';
            block += `<div class="price-info-item"><div class="price-box-diff-margin ${cls}" style="margin-top: 5px; ${marginBoxStyle}"><p>Narzut: ${formattedMarginValue} (${sign}${formattedMarginPercent}%)</p></div></div>`;
        } else if (marginPrice != null) {
            block += `<div class="price-info-item"><div class="price-box-diff-margin" style="margin-top: 5px;"><p>Cena zakupu: ${formatPricePL(marginPrice)}</p></div></div>`;
        }
        block += '</div>';
        return block;
    }

    function calculateMarginJS(price, marginPrice, commission, includeCommission) {
        if (marginPrice === null || price === null) {
            return { marginAmount: null, marginPercent: null };
        }
        const priceNum = parseFloat(price);
        const marginPriceNum = parseFloat(marginPrice);
        if (marginPriceNum === 0) {
            return { marginAmount: null, marginPercent: null };
        }
        const commissionNum = (includeCommission && commission !== null) ? parseFloat(commission) : 0;
        const netPrice = priceNum - commissionNum;
        const marginAmount = netPrice - marginPriceNum;
        const marginPercent = (marginAmount / marginPriceNum) * 100;
        return {
            marginAmount: marginAmount,
            marginPercent: marginPercent
        };
    }

    function renderHistoryView(batches) {
        const historyContainer = document.getElementById("historyModalBody");
        if (!historyContainer) return;

        let totalItems = 0;
        if (!batches || batches.length === 0) {
            historyContainer.innerHTML = '<p style="text-align: center; padding: 20px; font-size: 16px;">Brak historii wgranych zmian dla tej analizy.</p>';
            document.getElementById('historyTabCounter').textContent = 0;
            return;
        }

        let html = '';
        batches.forEach(batch => {
            totalItems += batch.items.length;
            const executionDate = new Date(batch.executionDate).toLocaleString('pl-PL');

            html += `
            <div class="history-batch-header">
                <strong>Paczka z dnia:</strong> ${executionDate} | 
                <strong>Wgrał:</strong> ${batch.userName} | 
                <strong style="color: #28a745;">Sukces: ${batch.successfulCount}</strong> | 
                <strong style="color: #dc3545;">Błędy: ${batch.failedCount}</strong>
            </div>
            <table class="table-orders" style="margin-bottom: 20px;">
                <thead>
                    <tr>
                        <th>Produkt</th>
                        <th>Przed zmianą</th>
                        <th>Symulacja</th>
                        <th>Weryfikacja API</th>
                        <th>Status</th>
                    </tr>
                </thead>
                <tbody>
            `;

            batch.items.forEach(item => {
                const marginBefore = calculateMarginJS(item.priceBefore, item.marginPrice, item.commissionBefore, item.includeCommissionInMargin);
                const marginSimulated = calculateMarginJS(item.priceAfter_Simulated, item.marginPrice, item.commissionBefore, item.includeCommissionInMargin);
                const marginVerified = calculateMarginJS(item.priceAfter_Verified, item.marginPrice, item.commissionAfter_Verified, item.includeCommissionInMargin);

                const blockBefore = `
                <div class="price-info-box small-text">
                    <div class="price-info-item">Cena: <strong>${formatPricePL(item.priceBefore)}</strong></div>
                    <div class="price-info-item">Prowizja: ${formatPricePL(item.commissionBefore)}</div>
                    <div class="price-info-item">Ranking: ${item.rankingBefore || '-'}</div>
                    <div class="price-info-item">Narzut: ${formatPricePL(marginBefore.marginAmount)} (${marginBefore.marginPercent !== null ? marginBefore.marginPercent.toFixed(2) + '%' : '-'})</div>
                </div>`;

                const blockSimulated = `
                <div class="price-info-box small-text">
                    <div class="price-info-item">Cena: <strong>${formatPricePL(item.priceAfter_Simulated)}</strong></div>
                    <div class="price-info-item">Prowizja: ${formatPricePL(item.commissionBefore)} (Szac.)</div>
                    <div class="price-info-item">Ranking: ${item.rankingAfter_Simulated || '-'}</div>
                    <div class="price-info-item">Narzut: ${formatPricePL(marginSimulated.marginAmount)} (${marginSimulated.marginPercent !== null ? marginSimulated.marginPercent.toFixed(2) + '%' : '-'})</div>
                </div>`;

                const blockVerified = item.success ? `
                <div class="price-info-box small-text" style="background: #dff0d8; border-color: #c1e2b3;">
                    <div class="price-info-item">Cena: <strong>${formatPricePL(item.priceAfter_Verified)}</strong></div>
                    <div class="price-info-item">Prowizja: ${formatPricePL(item.commissionAfter_Verified)} (Real.)</div>
                    <div class="price-info-item">Ranking: ${item.rankingAfter_Simulated || '-'}</div>
                    <div class="price-info-item">Narzut: ${formatPricePL(marginVerified.marginAmount)} (${marginVerified.marginPercent !== null ? marginVerified.marginPercent.toFixed(2) + '%' : '-'})</div>
                </div>` :
                    '<div class="price-info-box small-text" style="background: #f8d7da; border-color: #f5c6cb;"><p>Weryfikacja nieudana.</p></div>';

                const statusBlock = item.success ?
                    '<span style="color: #28a745; font-weight: bold;">Wgrano</span>' :
                    `<span style="color: #dc3545; font-weight: bold;" title="${item.errorMessage || ''}">Błąd</span>`;

                let idInfo = item.offerId ? `<div class="price-info-item small-text">ID: ${item.offerId}</div>` : `<div class="price-info-item small-text" style="color:#888;">ID: Brak</div>`;
                let eanInfo = item.ean ? `<div class="price-info-item small-text">EAN: ${item.ean}</div>` : `<div class="price-info-item small-text" style="color:#888;">EAN: Brak</div>`;

                let allegroButton = '';

               
                const offerUrl = item.allegroOfferUrl ? item.allegroOfferUrl : (item.offerId ? `https://allegro.pl/oferta/${item.offerId}` : null);

                if (offerUrl) {
            
                    allegroButton = `
                    <a href="${offerUrl}" class="Button-Page-Small" style="margin-top: 8px;" target="_blank" rel="noopener noreferrer" onclick="event.stopPropagation();">
                        <img src="/images/AllegroIcon.png" alt="Allegro Icon" style="width:18px; height:18px; margin-right: 5px;"> Zobacz
                    </a>`;
                }

                html += `
                <tr>
                <td class="align-middle">
                    <div class="price-info-item" style="font-weight: 500;">${item.productName}</div>
                    ${idInfo}
                    ${eanInfo}
                    ${allegroButton}
                </td>
                <td class="align-middle">${blockBefore}</td>
                <td class="align-middle">${blockSimulated}</td>
                <td class="align-middle">${blockVerified}</td>
                <td class="align-middle text-center">${statusBlock}</td>
                </tr>`;
            });

       
            html += `</tbody></table>`;
        });

        historyContainer.innerHTML = html;
        document.getElementById('historyTabCounter').textContent = totalItems;
    }

    let historyLoaded = false;

    function fetchAndRenderHistory() {

        if (typeof globalAllegroScrapId === 'undefined' || globalAllegroScrapId === 0) {
            document.getElementById("historyModalBody").innerHTML = '<p style="text-align: center; padding: 20px;">Nie można załadować historii (Brak ID scrapu).</p>';
            document.getElementById('historyTabCounter').textContent = '0';
            return;
        }

        if (historyLoaded) return;
        historyLoaded = true;

        const historyContainer = document.getElementById("historyModalBody");
        historyContainer.innerHTML = '<div class="loader" style="margin: 20px auto;"></div>';

  
        fetch(`/AllegroPriceHistory/GetScrapPriceChangeHistory?storeId=${storeId}&allegroScrapeHistoryId=${globalAllegroScrapId}`)
            .then(response => {
                if (!response.ok) {
                    historyLoaded = false;
            
                    throw new Error(`Błąd HTTP! status: ${response.status}`);
                }
                return response.json();
            })
            .then(batches => {
                renderHistoryView(batches);
            })
            .catch(err => {
                historyLoaded = false;
                console.error("Błąd pobierania historii zmian:", err);
           
                historyContainer.innerHTML = `<p style="text-align: center; padding: 20px; font-size: 16px; color: red;">Wystąpił błąd podczas ładowania historii: ${err.message}</p>`;
                document.getElementById('historyTabCounter').textContent = 'X';
            });
    }

    function openSimulationModal() {

        historyLoaded = false;
        document.getElementById('simulationModalBody').style.display = 'block';
        document.getElementById('historyModalBody').style.display = 'none';

        const cartTab = document.getElementById('showPriceChangeCart');
        const historyTab = document.getElementById('showPriceChangeHistory');
        if (cartTab && historyTab) {
            cartTab.classList.add('button-active');
            cartTab.classList.remove('button-inactive');
            historyTab.classList.add('button-inactive');
            historyTab.classList.remove('button-active');
        }

        document.getElementById('historyModalBody').innerHTML = '<p style="text-align: center; padding: 20px;">Ładowanie historii...</p>';

        updatePriceChangeSummary();
        document.getElementById('historyTabCounter').textContent = '...';

        fetchAndRenderHistory();

        if (selectedPriceChanges.length === 0) {
            const tableContainer = document.getElementById("simulationModalBody");
            if (tableContainer) {
                tableContainer.innerHTML = '<p style="text-align: center; padding: 20px; font-size: 16px;">Brak produktów do symulacji.</p>';
            }

        } else {

            showLoading();

            var simulationData = selectedPriceChanges.map(function (item) {
                return {
                    ProductId: item.productId,
                    CurrentPrice: item.currentPrice,
                    NewPrice: item.newPrice,
                    StoreId: storeId
                };
            });

            fetch('/AllegroPriceHistory/GetPriceChangeDetails', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(selectedPriceChanges.map(i => i.productId))
            })
                .then(response => {
                    if (!response.ok) throw new Error(`HTTP error! status: ${response.status} fetching product details`);
                    return response.json();
                })
                .then(productDetails => {
                    var hasImage = productDetails.some(prod => prod.imageUrl && prod.imageUrl.trim() !== "");
                    return fetch('/AllegroPriceHistory/SimulatePriceChange', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(simulationData)
                    })
                        .then(response => {
                            if (!response.ok) {
                                return response.text().then(text => { throw new Error(`HTTP error! status: ${response.status}, message: ${text}`); });
                            }
                            return response.json();
                        })
                        .then(data => {
                            return { productDetails, data, hasImage };
                        });
                })
                .then(({ productDetails, data, hasImage }) => {
                    globalIncludeCommissionSetting = data.allegroIncludeCommisionInPriceChange === true;
                    usePriceWithDeliverySetting = data.usePriceWithDelivery === true;
                    if (data.ourStoreName) {
                        currentOurStoreName = data.ourStoreName;
                    }
                    if (data.latestScrapId) {
                        globalLatestScrapId = data.latestScrapId;
                    }

                    var simulationResults = data.simulationResults || [];
                    let tableHtml = '<table class="table-orders" id="simulationTable"><thead><tr>';
                    if (hasImage) {
                        tableHtml += '<th>Produkt</th>';
                    }
                    tableHtml += '<th></th>';
                    tableHtml += '<th>Obecne</th>';
                    tableHtml += '<th>Zmiana</th>';
                    tableHtml += '<th>Po zmianie</th>';
                    tableHtml += '<th>Potwierdzenie API</th>';
                    tableHtml += '<th>Opłacalność zmiany</th>';
                    tableHtml += '<th>Usuń</th>';
                    tableHtml += '</tr></thead><tbody id="simulationTbody"></tbody></table>';
                    const tableContainer = document.getElementById("simulationModalBody");
                    if (tableContainer) {
                        tableContainer.innerHTML = tableHtml;
                    }
                    originalRowsData = selectedPriceChanges.map(function (item, index) {
                        const productIdStr = String(item.productId);
                        const prodDetail = productDetails.find(x => String(x.productId) === productIdStr);
                        const simResult = simulationResults.find(x => String(x.productId) === productIdStr);
                        const marginPrice = simResult ? simResult.marginPrice : null;
                        const apiAllegroCommission = simResult ? simResult.apiAllegroCommission : null;
                        const myIdAllegro = item.myIdAllegro;
                        const name = prodDetail ? prodDetail.productName : item.productName || 'Brak nazwy';
                        const imageUrl = prodDetail ? prodDetail.imageUrl : "";
                        const ean = simResult ? simResult.ean : (prodDetail ? String(prodDetail.ean || '') : '');
                        const allegroOfferUrl = simResult ? simResult.allegroOfferUrl : (prodDetail ? prodDetail.allegroOfferUrl : null);
                        const externalId = simResult ? simResult.externalId : null;
                        const producerCode = simResult ? simResult.producerCode : null;
                        const currentPriceNum = parseFloat(item.currentPrice);
                        const newPriceNum = parseFloat(item.newPrice);
                        let diff = newPriceNum - currentPriceNum;
                        let diffPercent = (currentPriceNum > 0) ? (diff / currentPriceNum) * 100 : 0;
                        let arrow = '<span style="color: gray;">●</span>';
                        if (diff > 0.005) arrow = '<span style="color: red;">▲</span>';
                        else if (diff < -0.005) arrow = '<span style="color: green;">▼</span>';
                        let currentBlock = buildPriceBlock(
                            simResult ? simResult.baseCurrentPrice : null,
                            marginPrice,
                            apiAllegroCommission,
                            simResult ? simResult.currentAllegroRanking : null,
                            simResult ? simResult.totalAllegroOffers : null
                        );
                        let newBlock = buildPriceBlock(
                            simResult ? simResult.baseNewPrice : null,
                            marginPrice,
                            apiAllegroCommission,
                            simResult ? simResult.newAllegroRanking : null,
                            simResult ? simResult.totalAllegroOffers : null
                        );
                        let opp = computeOpportunityScore(item, simResult);
                        let bestScore = (opp && opp.allegroFinalScore != null) ? opp.allegroFinalScore : 0;
                        let effectDetails = "";
                        if (opp.basePriceChangeType === 'decrease') {
                            if (opp.allegroFinalScore != null) {
                                effectDetails += createDonutChart(opp.allegroFinalScore, "/images/AllegroIcon.png");
                            } else {
                                // Poprawiono błąd składni: usunięto spacje w tagach
                                effectDetails += `<div><span style="color: green; font-weight: 400; font-size: 16px;">▼</span> <span style="color: #222222; font-weight: 400; font-size: 16px;"> Obniżka</span></div>`;
                            }
                        } else if (opp.basePriceChangeType === 'increase') {
                            // Poprawiono błąd składni: usunięto spacje w tagach
                            effectDetails += `<div><span style="color: red; font-weight: 400; font-size: 16px;">▲</span> <span style="color: #222222; font-weight: 400; font-size: 16px;"> Podwyżka</span></div>`;
                        } else {
                            // Poprawiono błąd składni: usunięto spacje w tagach
                            effectDetails += `<div><span style="color: gray; font-weight: 400; font-size: 16px;">●</span> <span style="color: #222222; font-weight: 400; font-size: 16px;"> Bez zmian</span></div>`;
                        }
                        return {
                            index, hasImage, imageUrl, productName: name, ean, externalId, producerCode,
                            diff, diffPercent, arrow, currentBlock, newBlock, finalScore: bestScore,
                            effectDetails, productId: productIdStr, scrapId: globalLatestScrapId, allegroOfferUrl: allegroOfferUrl,
                            myIdAllegro: myIdAllegro, marginPrice: marginPrice,
                            apiAllegroCommission: apiAllegroCommission,
                            baseCurrentPrice: simResult ? simResult.baseCurrentPrice : null,
                            baseNewPrice: simResult ? simResult.baseNewPrice : null,
                            effectiveCurrentPrice: item.currentPrice,
                            effectiveNewPrice: item.newPrice,
                            currentMargin: simResult ? simResult.currentMargin : null,
                            currentMarginValue: simResult ? simResult.currentMarginValue : null,
                            newMargin: simResult ? simResult.newMargin : null,
                            newMarginValue: simResult ? simResult.newMarginValue : null,
                            currentAllegroRanking: simResult ? simResult.currentAllegroRanking : null,
                            newAllegroRanking: simResult ? simResult.newAllegroRanking : null,
                            totalAllegroOffers: simResult ? simResult.totalAllegroOffers : null
                        };
                    });
                    renderRows(originalRowsData);
                })
                .catch(function (err) {
                    console.error("Błąd pobierania danych produktu / symulacji:", err);
                    hideLoading();
                    const tableContainer = document.getElementById("simulationModalBody");
                    if (tableContainer) {
                       
                        tableContainer.innerHTML = `<p style="text-align: center; padding: 20px; font-size: 16px; color: red;">Wystąpił błąd: ${err.message}. Spróbuj ponownie.</p>`;
                    }
                })
                .finally(function () {
                    hideLoading();
                });
        }

        const simulationModal = document.getElementById("simulationModal");
        if (simulationModal) {
            simulationModal.style.display = 'block';
            simulationModal.classList.add('show');
        }
    }

    function renderRows(rows) {
        const tbody = document.getElementById("simulationTbody");
        if (!tbody) return;
        let html = "";
        rows.forEach(row => {
            let imageCell = "";
            if (row.hasImage) {
                const imgSrc = (row.imageUrl && row.imageUrl.trim() !== "") ? row.imageUrl : '/images/placeholder.png';
         
                imageCell = `
                    <td>
                    <div class="price-info-item" style="padding:4px; text-align: center;"> <img
                        data-src="${imgSrc}" alt="${row.productName}"
                        class="lazy-load" style="width: 114px; height: 114px; object-fit: contain; background-color: #fff; border: 1px solid #e3e3e3; border-radius: 4px; padding: 5px; display: inline-block;"
                        src="${imgSrc}">
                    </div>
                    </td>`;
            }

           
            let idInfo = row.myIdAllegro ? `<div class="price-info-item small-text">ID: ${row.myIdAllegro}</div>` : `<div class="price-info-item small-text" style="color:#888;">ID: Brak</div>`;
            let eanInfo = row.ean ? `<div class="price-info-item small-text">EAN: ${row.ean}</div>` : `<div class="price-info-item small-text" style="color:#888;">EAN: Brak</div>`;

            let allegroButton = '';

            const offerUrl = row.allegroOfferUrl ? row.allegroOfferUrl : (row.myIdAllegro ? `https://allegro.pl/oferta/${row.myIdAllegro}` : null);

            if (offerUrl) {
                allegroButton = `
                <a href="${offerUrl}" class="Button-Page-Small" style="margin-top: 8px;" target="_blank" rel="noopener noreferrer" onclick="event.stopPropagation();">
                    <img src="/images/AllegroIcon.png" alt="Allegro Icon" style="width:18px; height:18px; margin-right: 5px;"> Zobacz
                </a>`;
            }

            const formattedDiff = formatPricePL(Math.abs(row.diff), false);
            const formattedDiffPercent = Math.abs(row.diffPercent).toFixed(2);

            html += `<tr data-product-id="${row.productId}" data-offer-id="${row.myIdAllegro || ''}">
                        ${imageCell}
                        <td class="align-middle">
                            <a
                                href="/AllegroPriceHistory/Details?storeId=${storeId}&productId=${row.productId}"
                                target="_blank"
                                rel="noopener noreferrer"
                                class="simulationProductTitle"
                                title="Zobacz szczegóły produktu"
                                style="text-decoration: none; color: inherit;"
                            >
                                <div class="price-info-item" style="font-size:110%; margin-bottom:8px; font-weight: 500;">${row.productName}</div>
                            </a>
                            ${idInfo}
                            ${eanInfo}
                            ${allegroButton}
                        </td>
                        <td class="align-middle">${row.currentBlock}</td>
                        <td class="align-middle" style="font-size: 1em; white-space: nowrap;">
                            <div>${row.arrow} ${formattedDiff} PLN</div>
                            <div style="font-size: 0.9em; color: #555; margin-left:19px;">(${formattedDiffPercent}%)</div>
                        </td>
                        <td class="align-middle">${row.newBlock}</td>
                        <td class="align-middle" id="confirm_${row.productId}" style="min-width: 200px;">
                        </td>
                        <td class="align-middle" style="white-space: normal; text-align: center;">${row.effectDetails}</td>
                        <td class="align-middle text-center">
                            <button class="remove-change-btn"
                                data-product-id="${row.productId}"
                                title="Usuń tę zmianę z symulacji"
                                style="background: none; border: none; padding: 5px; display: inline-flex; align-items: center; justify-content: center; cursor: pointer;">
                                <i class="fa fa-trash" style="font-size: 19px; color: #555;"></i>
                            </button>
                        </td>
                    </tr>`;
        });
        tbody.innerHTML = html;
        tbody.querySelectorAll('.remove-change-btn').forEach(btn => {
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                const prodId = this.getAttribute('data-product-id');
                const removeEvent = new CustomEvent('priceBoxChangeRemove', {
                    detail: { productId: prodId }
                });
                document.dispatchEvent(removeEvent);
            });
        });
    }

    function sortRowsByScoreDesc(rows) {
        return rows.sort((a, b) => {
            const scoreA = a.finalScore !== null ? a.finalScore : -1;
            const scoreB = b.finalScore !== null ? b.finalScore : -1;
            return scoreB - scoreA;
        });
    }

    if (clearChangesButton) {
        clearChangesButton.addEventListener("click", clearPriceChanges);
    }

    const showCartTab = document.getElementById('showPriceChangeCart');
    const showHistoryTab = document.getElementById('showPriceChangeHistory');
    const simulationModalBody = document.getElementById('simulationModalBody');
    const historyModalBody = document.getElementById('historyModalBody');

    if (showCartTab && showHistoryTab && simulationModalBody && historyModalBody) {
        showCartTab.addEventListener('click', function () {
            simulationModalBody.style.display = 'block';
            historyModalBody.style.display = 'none';

            showCartTab.classList.add('button-active');
            showCartTab.classList.remove('button-inactive');
            showHistoryTab.classList.add('button-inactive');
            showHistoryTab.classList.remove('button-active');
        });

        showHistoryTab.addEventListener('click', function () {
            simulationModalBody.style.display = 'none';
            historyModalBody.style.display = 'block';

            showCartTab.classList.add('button-inactive');
            showCartTab.classList.remove('button-active');
            showHistoryTab.classList.add('button-active');
            showHistoryTab.classList.remove('button-inactive');

            fetchAndRenderHistory();
        });
    }

    var simulateButton = document.getElementById("simulateButton");
    if (simulateButton) {
        simulateButton.addEventListener("click", openSimulationModal);
    }

    const executeButton = document.getElementById("executePriceChangeBtn");
    if (executeButton) {
        executeButton.addEventListener("click", function () {
            const activeProductIds = new Set(selectedPriceChanges.map(c => c.productId));
            const itemsToBridge = originalRowsData
                .filter(row => activeProductIds.has(row.productId) && row.myIdAllegro)
                .map(row => ({
                    ProductId: parseInt(row.productId, 10),
                    OfferId: row.myIdAllegro.toString(),
                    MarginPrice: row.marginPrice,
                    IncludeCommissionInMargin: globalIncludeCommissionSetting,
                    PriceBefore: row.baseCurrentPrice,
                    CommissionBefore: row.apiAllegroCommission,
                    RankingBefore: row.currentAllegroRanking,
                    PriceAfter_Simulated: row.baseNewPrice,
                    RankingAfter_Simulated: row.newAllegroRanking,
                }));

            const changesWithoutId = selectedPriceChanges.filter(c => !c.myIdAllegro).length;
            if (itemsToBridge.length === 0) {
                alert("Żadna z wybranych zmian nie ma przypisanego ID oferty Allegro. Nie można wgrać zmian.");
                return;
            }
            if (!confirm(`Zostanie wgranych ${itemsToBridge.length} zmian cen na Allegro.\n\n${changesWithoutId > 0 ? `Pominiętych zostanie ${changesWithoutId} zmian (brak ID oferty).\n` : ''}\nTa operacja jest NIEODWRACALNA. Kontynuować?`)) {
                return;
            }
            showLoading();
            executeButton.disabled = true;
            executeButton.innerHTML = '<i class="fa-solid fa-spinner fa-spin" style="margin-right: 8px;"></i> Wgrywanie...';

            fetch(`/AllegroPriceHistory/ExecutePriceChange?storeId=${storeId}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(itemsToBridge)
            })
                .then(response => {
                    if (!response.ok) {
                        return response.text().then(text => { throw new Error(`Błąd serwera: ${response.status} - ${text}`); });
                    }
                    return response.json();
                })
                .then(result => {
                    hideLoading();
                    executeButton.disabled = false;
                    executeButton.innerHTML = '<i class="fa-solid fa-arrow-up-from-bracket" style="margin-right: 8px;"></i> Wgraj zmiany na Allegro';
                    let message = `<p style="font-weight:bold; font-size:16px;">Operacja zakończona!</p>`;
                    message += `<p>Wysłano ${itemsToBridge.length} ofert do aktualizacji.</p>`;
                    message += `<p style="color: #c8e6c9;">Pomyślnie zaktualizowano: ${result.successfulCount}</p>`;
                    message += `<p style="color: #ffcdd2;">Błędy: ${result.failedCount}</p>`;
                    if (changesWithoutId > 0) {
                        message += `<p style="color: #ffe0b2;">Pominięto (brak ID): ${changesWithoutId}</p>`;
                    }
                    if (result.failedCount > 0 && result.errors && result.errors.length > 0) {
                        message += `<br/><p style="font-weight:bold;"><u>Szczegóły błędów:</u></p>`;
                        message += `<ul style="font-size:12px; max-height: 100px; overflow-y: auto; list-style-type: none; padding-left: 0;">`;
                        result.errors.forEach(err => {
                            message += `<li style="margin-bottom: 5px;"><strong>${err.offerId}:</strong> ${err.message}</li>`;
                        });
                        message += `</ul>`;
                    }
                    if (typeof showGlobalUpdate === 'function') {
                        showGlobalUpdate(message);
                    } else {
                        alert(`Zakończono.\nSukcesy: ${result.successfulCount}\nBłędy: ${result.failedCount}\nPominięto: ${changesWithoutId}`);
                    }
                    if (result.successfulChangesDetails && result.successfulChangesDetails.length > 0) {
                        const successfulProductIds = new Set();
                        result.successfulChangesDetails.forEach(detail => {
                            const rowData = originalRowsData.find(r => r.myIdAllegro && r.myIdAllegro.toString() === detail.offerId);
                            if (!rowData) {
                                console.warn("Nie znaleziono rowData dla offerId:", detail.offerId);
                                return;
                            }
                            successfulProductIds.add(rowData.productId);
                            const cell = document.getElementById(`confirm_${rowData.productId}`);
                            if (!cell) {
                                console.warn("Nie znaleziono komórki confirm_ dla productId:", rowData.productId);
                                return;
                            }
                            const confirmedPrice = detail.fetchedNewPrice;
                            const confirmedCommission = detail.fetchedNewCommission;
                            const marginPrice = rowData.marginPrice;
                            const newAllegroRanking = rowData.newAllegroRanking;
                            const totalAllegroOffers = rowData.totalAllegroOffers;
                            cell.innerHTML = buildPriceBlock(
                                confirmedPrice, marginPrice, confirmedCommission,
                                newAllegroRanking, totalAllegroOffers, true
                            );
                            const rowElement = document.querySelector(`tr[data-product-id="${rowData.productId}"]`);
                            if (rowElement) {
                                const removeBtn = rowElement.querySelector('.remove-change-btn');
                                if (removeBtn) {
                                    removeBtn.disabled = true;
                                    removeBtn.style.opacity = 0.3;
                                    removeBtn.style.cursor = 'not-allowed';
                                    removeBtn.title = 'Zmiana została wgrana na Allegro.';
                                }
                            }
                        });
                        selectedPriceChanges = selectedPriceChanges.filter(c =>
                            !successfulProductIds.has(c.productId)
                        );
                        originalRowsData = originalRowsData.filter(row =>
                            !successfulProductIds.has(row.productId)
                        );
                        savePriceChanges();
                        updatePriceChangeSummary();
                        if (typeof window.refreshPriceBoxStates === 'function') {
                            window.refreshPriceBoxStates();
                        }
                    }
                    if (selectedPriceChanges.length === 0 && result.failedCount === 0) {
                        $('#simulationModal').modal('hide');
                    }
                })
                .catch(err => {
                    hideLoading();
                    executeButton.disabled = false;
                    executeButton.innerHTML = '<i class="fa-solid fa-arrow-up-from-bracket" style="margin-right: 8px;"></i> Wgraj zmiany na Allegro';
                    console.error("Błąd fetch ExecutePriceChange:", err);
                    let errorMsg = `<p style='font-weight:bold;'>Błąd krytyczny</p><p>${err.message}</p>`;
                    if (typeof showGlobalNotification === 'function') {
                        showGlobalNotification(errorMsg);
                    } else {
                        alert("Błąd krytyczny. Nie udało się wgrać zmian.\n" + err.message);
                    }
                });
        });
    }

    const bestScoreBtn = document.getElementById("bestScoreButton");
    if (bestScoreBtn) {
        bestScoreBtn.addEventListener("click", function () {
            const tableBody = document.getElementById("simulationTbody");
            if (!tableBody || originalRowsData.length === 0) return;
            let sorted = sortRowsByScoreDesc([...originalRowsData]);
            renderRows(sorted);
        });
    }

    var closeSimulationModalButtons = document.querySelectorAll('#simulationModal .close, #simulationModal [data-dismiss="modal"]');
    closeSimulationModalButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            var simulationModal = document.getElementById("simulationModal");
            if (simulationModal) {
                simulationModal.style.display = 'none';
                simulationModal.classList.remove('show');
            }
            if (typeof window.refreshPriceBoxStates === 'function') {
                window.refreshPriceBoxStates();
            }
        });
    });

    window.addEventListener('click', function (event) {
        const simulationModal = document.getElementById("simulationModal");
        if (event.target == simulationModal) {
            if (simulationModal) {
                simulationModal.style.display = 'none';
                simulationModal.classList.remove('show');
                if (typeof window.refreshPriceBoxStates === 'function') {
                    window.refreshPriceBoxStates();
                }
            }
        }
    });

    loadChangesFromStorage();
    updatePriceChangeSummary();

});