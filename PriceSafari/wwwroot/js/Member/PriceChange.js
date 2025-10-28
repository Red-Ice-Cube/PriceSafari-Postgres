document.addEventListener("DOMContentLoaded", function () {

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
        const percentage = score;
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

    const localStorageKey = 'selectedPriceChanges_' + storeId;
    var selectedPriceChanges = [];
    var sessionScrapId = null;
    const storedDataJSON = localStorage.getItem(localStorageKey);
    if (storedDataJSON) {
        try {
            const storedData = JSON.parse(storedDataJSON);
            if (storedData && storedData.scrapId && Array.isArray(storedData.changes)) {

                if (storedData.changes.length > 0 && typeof storedData.changes[0].stepPriceApplied === 'undefined') {
                    console.warn("Wykryto stary format danych w LS (bez kroku cenowego). Zostanie on usunięty przy następnej zmianie.");
                    selectedPriceChanges = storedData.changes;
                    sessionScrapId = storedData.scrapId;
                } else {
                    selectedPriceChanges = storedData.changes;
                    sessionScrapId = storedData.scrapId;
                }
            } else {
                console.warn("Nieznany lub stary format danych w LS. Zostanie on usunięty przy następnej zmianie.");
                localStorage.removeItem(localStorageKey);
            }
        } catch (err) {
            console.error("Błąd parsowania storedChanges z Local Storage:", err);
            localStorage.removeItem(localStorageKey);
        }
    }

    let pendingExportType = null;
    let currentOurStoreName = storeId || "Sklep";
    let globalLatestScrapId = null;
    let usePriceWithDeliverySetting = false;

    let eanMapGlobal = {};
    let externalIdMapGlobal = {};

    let originalRowsData = [];

    function getCurrentDateString() {
        const d = new Date();

        return d.toISOString().split('T')[0];
    }

    function updatePriceChangeSummary(forceClear = false) {
        if (forceClear) {
            selectedPriceChanges = [];

        }
        var increasedCount = selectedPriceChanges.filter(function (item) {

            return parseFloat(item.newPrice) > parseFloat(item.currentPrice);
        }).length;
        var decreasedCount = selectedPriceChanges.filter(function (item) {

            return parseFloat(item.newPrice) < parseFloat(item.currentPrice);
        }).length;
        var totalCount = selectedPriceChanges.length;

        var summaryText = document.getElementById("summaryText");
        if (summaryText) {

            summaryText.innerHTML =
                `<div class='price-change-up' style='display: inline-block;'><span style='color: red;'>▲</span> ${increasedCount}</div>` +
                `<div class='price-change-down' style='display: inline-block;'><span style='color: green;'>▼</span> ${decreasedCount}</div>`;
        }

        var simulateButton = document.getElementById("simulateButton");
        if (simulateButton) {
            simulateButton.disabled = (totalCount === 0);
            simulateButton.style.opacity = (totalCount === 0 ? '0.5' : '1');
            simulateButton.style.cursor = (totalCount === 0 ? 'not-allowed' : 'pointer');
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

    const clearChangesButton = document.getElementById("clearChangesButton");
    if (clearChangesButton) {
        clearChangesButton.addEventListener("click", function () {

            clearPriceChanges();

        });
    }

    document.addEventListener('priceBoxChange', function (event) {
        const { productId, productName, currentPrice, newPrice, scrapId, stepPriceApplied, stepUnitApplied } = event.detail;

        if (selectedPriceChanges.length === 0) {
            sessionScrapId = scrapId;
        } else if (sessionScrapId !== scrapId) {
            console.warn(`ScrapId w sesji (${sessionScrapId}) różni się od scrapId eventu (${scrapId}). Czyszczenie starych zmian.`);
            selectedPriceChanges = [];
            sessionScrapId = scrapId;
        }

        var existingIndex = selectedPriceChanges.findIndex(function (item) {
            return String(item.productId) === String(productId);
        });

        const changeData = {
            productId: String(productId),
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

        selectedPriceChanges = selectedPriceChanges.filter(function (item) {
            return item.productId !== productIdStr;
        });

        savePriceChanges();
        updatePriceChangeSummary();

        const rowElement = document.querySelector(`#simulationTbody tr button[data-product-id='${productIdStr}']`)?.closest('tr');
        if (rowElement) {
            rowElement.remove();
        }

        originalRowsData = originalRowsData.filter(function (rowItem) {
            return String(rowItem.productId) !== productIdStr;
        });

        if (selectedPriceChanges.length === 0) {
            const tableContainer = document.getElementById("simulationModalBody");
            const simulationTable = document.getElementById("simulationTable");
            if (tableContainer && simulationTable) {
                simulationTable.remove();
                tableContainer.innerHTML = '<p style="text-align: center; padding: 20px; font-size: 16px;">Brak produktów do symulacji.</p>';
            }
            sessionScrapId = null;
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
        let relevantPart = (slashIndex !== -1)
            ? rankStr.substring(0, slashIndex).trim()
            : rankStr.trim();

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

        return {
            rank: rankVal,
            isRange: isRange,
            rangeSize: rangeSize
        };
    }

    function computeOpportunityScore(item, simResult) {

        if (!simResult) {
            return {
                cost: 0,
                costFrac: 0,
                googleGained: 0,
                ceneoGained: 0,
                googleFinalScore: null,
                ceneoFinalScore: null,
                basePriceChangeType: 'unknown'
            };
        }

        const basePriceDiff = (simResult.baseNewPrice ?? 0) - (simResult.baseCurrentPrice ?? 0);
        let basePriceChangeType = 'none';
        if (basePriceDiff > 0.005) basePriceChangeType = 'increase';
        else if (basePriceDiff < -0.005) basePriceChangeType = 'decrease';

        let googleOldData = parseRankingString(String(simResult.currentGoogleRanking));
        let googleNewData = parseRankingString(String(simResult.newGoogleRanking));
        let totalG = simResult.totalGoogleOffers ? parseInt(simResult.totalGoogleOffers, 10) : 0;

        let ceneoOldData = parseRankingString(String(simResult.currentCeneoRanking));
        let ceneoNewData = parseRankingString(String(simResult.newCeneoRanking));
        let totalC = simResult.totalCeneoOffers ? parseInt(simResult.totalCeneoOffers, 10) : 0;

        let effectivePriceDiff = item.newPrice - item.currentPrice;
        let cost = Math.abs(effectivePriceDiff);
        let costFrac = 0;
        if (item.currentPrice > 0) {
            costFrac = cost / item.currentPrice;
        }

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

            const rangeUncertaintyModifier = 0.8;
            if (isOldRankRangeImprovement && gainedPos > 0) {
                jumpFrac *= rangeUncertaintyModifier;
            }

            let rankRounded = Math.round(newRankData.rank);
            let placeBonus = getPlaceBonus(rankRounded);

            if (newRankData.isRange && newRankData.rangeSize > 1) {
                placeBonus *= 0.4;
            }

            let effectiveCostFrac = (costFrac < 0.000001) ? 0.000001 : costFrac;
            let costFracPower = 0.5;
            let costFracAdjusted = Math.pow(effectiveCostFrac, costFracPower);
            let offset = 0.0001;

            let raw = (jumpFrac + placeBonus) / (costFracAdjusted + offset);

            let channelLogScale = 35;
            let val = Math.log10(raw + 1) * channelLogScale;

            val = Math.max(0, Math.min(100, val));

            return val;
        }

        const googleFinalScore = channelScore(googleOldData, googleNewData, totalG);
        const ceneoFinalScore = channelScore(ceneoOldData, ceneoNewData, totalC);

        let calculatedGoogleGained = 0;
        if (googleOldData.rank !== null && googleNewData.rank !== null) {
            let effectiveOldGoogleRank = googleOldData.rank;
            if (googleOldData.isRange && googleOldData.rangeSize > 1) {
                effectiveOldGoogleRank = googleOldData.rank + googleOldData.rangeSize - 1;
            }
            calculatedGoogleGained = effectiveOldGoogleRank - googleNewData.rank;
        }

        let calculatedCeneoGained = 0;
        if (ceneoOldData.rank !== null && ceneoNewData.rank !== null) {
            let effectiveOldCeneoRank = ceneoOldData.rank;
            if (ceneoOldData.isRange && ceneoOldData.rangeSize > 1) {
                effectiveOldCeneoRank = ceneoOldData.rank + ceneoOldData.rangeSize - 1;
            }
            calculatedCeneoGained = effectiveOldCeneoRank - ceneoNewData.rank;
        }

        return {
            cost,
            costFrac,

            googleGained: calculatedGoogleGained > 0 ? calculatedGoogleGained : 0,
            ceneoGained: calculatedCeneoGained > 0 ? calculatedCeneoGained : 0,
            googleFinalScore,
            ceneoFinalScore,
            basePriceChangeType
        };
    }

    function buildPriceBlock(basePrice, effectivePrice, shippingCost, usePriceWithDeliveryFlag, marginPercent, marginValue, googleRank, googleOffers, ceneoRank, ceneoOffers) {

        const formattedBasePrice = formatPricePL(basePrice);
        const formattedEffectivePrice = formatPricePL(effectivePrice);
        const formattedShippingCost = formatPricePL(shippingCost);

        let block = '<div class="price-info-box">';

        block += `
              <div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px; margin-bottom: 5px;">
                  Cena oferty | ${formattedBasePrice}
              </div>`;

        if (usePriceWithDeliveryFlag && effectivePrice !== null && shippingCost !== null && basePrice !== null) {
            block += `
                      <div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px; margin-bottom: 5px;">
                           Cena z wysyłką | ${formattedEffectivePrice}
                      </div>`;

            block += `
                      <div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px; margin-bottom: 5px;">
                          Składowe | ${formatPricePL(basePrice, false)} PLN + ${formatPricePL(shippingCost, false)} PLN
                      </div>`;
        }

        if (googleRank && googleRank !== "-") {
            const googleOffersText = googleOffers > 0 ? googleOffers : '-';
            block += `
              <div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px; margin-bottom: 5px;">
                  Poz. cenowa | <img src="/images/GoogleShopping.png" alt="Google Icon" style="width:16px; height:16px; vertical-align: middle; margin-right: 3px;" />
                  ${googleRank} / ${googleOffersText}
              </div>`;
        }

        if (ceneoRank && ceneoRank !== "-") {
            const ceneoOffersText = ceneoOffers > 0 ? ceneoOffers : '-';
            block += `
               <div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px; margin-bottom: 5px;">
                   Poz. cenowa | <img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:16px; height:16px; vertical-align: middle; margin-right: 3px;" />
                   ${ceneoRank} / ${ceneoOffersText}
               </div>`;
        }

        if (marginPercent != null && marginValue != null) {

            const formattedMarginValue = formatPricePL(marginValue);
            const formattedMarginPercent = parseFloat(marginPercent).toFixed(2);
            const sign = parseFloat(marginValue) >= 0 ? "+" : "";
            const cls = parseFloat(marginValue) >= 0
                ? "priceBox-diff-margin"
                : "priceBox-diff-margin-minus";
            block += `
                   <div class="price-info-item"> <div class="price-box-diff-margin ${cls}" style="margin-top: 5px;"> <p>Narzut: ${formattedMarginValue} (${sign}${formattedMarginPercent}%)</p>
                       </div>
                   </div>`;
        }

        block += '</div>';
        return block;
    }

    function openSimulationModal() {
        if (selectedPriceChanges.length === 0) {

            const tableContainer = document.getElementById("simulationModalBody");
            if (tableContainer) {
                tableContainer.innerHTML = '<p style="text-align: center; padding: 20px; font-size: 16px;">Brak produktów do symulacji.</p>';

                const simulationModal = document.getElementById("simulationModal");
                if (simulationModal) {
                    simulationModal.style.display = 'block';
                    simulationModal.classList.add('show');
                }
            }
            return;
        }

        showLoading();

        var simulationData = selectedPriceChanges.map(function (item) {
            return {
                ProductId: item.productId,
                CurrentPrice: item.currentPrice,
                NewPrice: item.newPrice,
                StoreId: storeId
            };
        });

        fetch('/PriceHistory/GetPriceChangeDetails', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ productIds: selectedPriceChanges.map(i => i.productId) })
        })
            .then(response => {
                if (!response.ok) {
                    throw new Error(`HTTP error! status: ${response.status} fetching product details`);
                }
                return response.json();
            })
            .then(productDetails => {

                var hasImage = productDetails.some(prod => prod.imageUrl && prod.imageUrl.trim() !== "");

                return fetch('/PriceHistory/SimulatePriceChange', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(simulationData)
                })
                    .then(response => {
                        if (!response.ok) {

                            return response.text().then(text => {
                                throw new Error(`HTTP error! status: ${response.status}, message: ${text}`);
                            });
                        }
                        return response.json();
                    })
                    .then(data => {

                        return { productDetails, data, hasImage };
                    });
            })
            .then(({ productDetails, data, hasImage }) => {

                usePriceWithDeliverySetting = data.usePriceWithDelivery === true;
                if (data.ourStoreName) {
                    currentOurStoreName = data.ourStoreName;
                }
                if (data.latestScrapId) {
                    globalLatestScrapId = data.latestScrapId;
                }

                var simulationResults = data.simulationResults || [];

                eanMapGlobal = {};
                externalIdMapGlobal = {};
                simulationResults.forEach(sim => {
                    const pidStr = String(sim.productId);
                    eanMapGlobal[pidStr] = sim.ean ? String(sim.ean) : "";
                    externalIdMapGlobal[pidStr] = sim.externalId ? String(sim.externalId) : "";
                });

                let tableHtml = '<table class="table-orders" id="simulationTable"><thead><tr>';
                if (hasImage) {
                    tableHtml += '<th>Produkt</th>';
                }
                tableHtml += '<th></th>';
                tableHtml += '<th>Obecne</th>';
                tableHtml += '<th>Zmiana</th>';
                tableHtml += '<th>Po zmianie</th>';
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

                    const name = prodDetail ? prodDetail.productName : item.productName || 'Brak nazwy';
                    const imageUrl = prodDetail ? prodDetail.imageUrl : "";
                    const ean = eanMapGlobal[productIdStr] || (prodDetail ? String(prodDetail.ean || '') : '');
                    const externalId = externalIdMapGlobal[productIdStr] || (prodDetail ? String(prodDetail.externalId || '') : '');
                    const producerCode = simResult ? simResult.producerCode : (prodDetail ? prodDetail.producerCode : null);

                    const currentPriceNum = parseFloat(item.currentPrice);
                    const newPriceNum = parseFloat(item.newPrice);
                    let diff = newPriceNum - currentPriceNum;
                    let diffPercent = 0;
                    if (currentPriceNum > 0) {
                        diffPercent = (diff / currentPriceNum) * 100;
                    }

                    let arrow = '<span style="color: gray;">●</span>';
                    if (diff > 0.005) arrow = '<span style="color: red;">▲</span>';
                    else if (diff < -0.005) arrow = '<span style="color: green;">▼</span>';

                    let currentBlock = buildPriceBlock(
                        simResult ? simResult.baseCurrentPrice : null,
                        item.currentPrice,
                        simResult ? simResult.ourStoreShippingCost : null,
                        usePriceWithDeliverySetting,
                        simResult ? simResult.currentMargin : null,
                        simResult ? simResult.currentMarginValue : null,
                        simResult ? simResult.currentGoogleRanking : null,
                        simResult ? simResult.totalGoogleOffers : null,
                        simResult ? simResult.currentCeneoRanking : null,
                        simResult ? simResult.totalCeneoOffers : null
                    );
                    let newBlock = buildPriceBlock(
                        simResult ? simResult.baseNewPrice : null,
                        item.newPrice,
                        simResult ? simResult.ourStoreShippingCost : null,
                        usePriceWithDeliverySetting,
                        simResult ? simResult.newMargin : null,
                        simResult ? simResult.newMarginValue : null,
                        simResult ? simResult.newGoogleRanking : null,
                        simResult ? simResult.totalGoogleOffers : null,
                        simResult ? simResult.newCeneoRanking : null,
                        simResult ? simResult.totalCeneoOffers : null
                    );

                    let opp = computeOpportunityScore(item, simResult);
                    let googleScore = (opp && opp.googleFinalScore != null) ? opp.googleFinalScore : 0;
                    let ceneoScore = (opp && opp.ceneoFinalScore != null) ? opp.ceneoFinalScore : 0;
                    let bestScore = Math.max(googleScore, ceneoScore);

                    let effectDetails = "";
                    if (opp.basePriceChangeType === 'decrease') {

                        const isPriceStepAdjustment = (opp.googleGained <= 0 && opp.ceneoGained <= 0) && (opp.googleFinalScore == null && opp.ceneoFinalScore == null);

                        if (isPriceStepAdjustment && Math.abs(diff) < 0.05) {
                            effectDetails += `<div>
                                           <span style="color: green; font-weight: 400; font-size: 16px;">▼</span>
                                           <span style="color: #222222; font-weight: 400; font-size: 16px;"> Obniżka krk. ceno.</span>
                                           </div>`;
                        } else {
                            if (opp.googleFinalScore != null) {
                                effectDetails += createDonutChart(opp.googleFinalScore, "/images/GoogleShopping.png");
                            }
                            if (opp.ceneoFinalScore != null) {
                                effectDetails += createDonutChart(opp.ceneoFinalScore, "/images/Ceneo.png");
                            }

                            if (opp.googleFinalScore == null && opp.ceneoFinalScore == null && !isPriceStepAdjustment) {
                                effectDetails += `<div>
                                                <span style="color: green; font-weight: 400; font-size: 16px;">▼</span>
                                                <span style="color: #222222; font-weight: 400; font-size: 16px;"> Obniżka</span>
                                                </div>`;
                            }
                        }
                    } else if (opp.basePriceChangeType === 'increase') {
                        effectDetails += `<div>
                                       <span style="color: red; font-weight: 400; font-size: 16px;">▲</span>
                                       <span style="color: #222222; font-weight: 400; font-size: 16px;"> Podwyżka ceny</span>
                                       </div>`;
                    } else {
                        effectDetails += `<div>
                                       <span style="color: gray; font-weight: 400; font-size: 16px;">●</span>
                                       <span style="color: #222222; font-weight: 400; font-size: 16px;"> Bez zmian</span>
                                       </div>`;
                    }

                    return {
                        index,
                        hasImage,
                        imageUrl,
                        productName: name,
                        ean,
                        externalId,
                        producerCode,
                        diff,
                        diffPercent,
                        arrow,
                        currentBlock,
                        newBlock,
                        finalScore: bestScore,
                        effectDetails,
                        productId: productIdStr,
                        scrapId: globalLatestScrapId,

                        baseCurrentPrice: simResult ? simResult.baseCurrentPrice : null,
                        baseNewPrice: simResult ? simResult.baseNewPrice : null,
                        effectiveCurrentPrice: item.currentPrice,
                        effectiveNewPrice: item.newPrice,
                        ourStoreShippingCost: simResult ? simResult.ourStoreShippingCost : null,
                        currentMargin: simResult ? simResult.currentMargin : null,
                        currentMarginValue: simResult ? simResult.currentMarginValue : null,
                        newMargin: simResult ? simResult.newMargin : null,
                        newMarginValue: simResult ? simResult.newMarginValue : null,

                        currentGoogleRanking: simResult ? simResult.currentGoogleRanking : null,
                        newGoogleRanking: simResult ? simResult.newGoogleRanking : null,
                        totalGoogleOffers: simResult ? simResult.totalGoogleOffers : null,
                        currentCeneoRanking: simResult ? simResult.currentCeneoRanking : null,
                        newCeneoRanking: simResult ? simResult.newCeneoRanking : null,
                        totalCeneoOffers: simResult ? simResult.totalCeneoOffers : null,
                    };
                });

                renderRows(originalRowsData);

                const simulationModal = document.getElementById("simulationModal");
                if (simulationModal) {
                    simulationModal.style.display = 'block';
                    simulationModal.classList.add('show');
                }

            })
            .catch(function (err) {
                console.error("Błąd pobierania danych produktu / symulacji:", err);
                hideLoading();

                const tableContainer = document.getElementById("simulationModalBody");
                if (tableContainer) {
                    tableContainer.innerHTML = `<p style="text-align: center; padding: 20px; font-size: 16px; color: red;">Wystąpił błąd podczas ładowania symulacji: ${err.message}. Spróbuj ponownie.</p>`;

                    const simulationModal = document.getElementById("simulationModal");
                    if (simulationModal) {
                        simulationModal.style.display = 'block';
                        simulationModal.classList.add('show');
                    }
                }
            })
            .finally(function () {

                hideLoading();
            });
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
                                src="${imgSrc}" >
                        </div>
                    </td>`;
            }

            let eanInfo = row.ean ? `<div class="price-info-item">EAN: ${row.ean}</div>` : "";
            let extIdInfo = row.externalId ? `<div class="price-info-item">ID: ${row.externalId}</div>` : "";
            let producerCodeInfo = row.producerCode ? `<div class="price-info-item">Kod: ${row.producerCode}</div>` : "";

            const formattedDiff = formatPricePL(Math.abs(row.diff), false);
            const formattedDiffPercent = Math.abs(row.diffPercent).toFixed(2);

            html += `<tr data-product-id="${row.productId}"> ${imageCell}
                           <td class="align-middle"> <a
                                   href="/PriceHistory/Details?scrapId=${row.scrapId}&productId=${row.productId}"
                                   target="_blank"
                                   rel="noopener noreferrer"
                                   class="simulationProductTitle"
                                   title="Zobacz szczegóły produktu"
                                   style="text-decoration: none; color: inherit;"
                               >
                                   <div class="price-info-item" style="font-size:110%; margin-bottom:8px; font-weight: 500;"> ${row.productName}
                                   </div>
                               </a>
                               ${eanInfo}
                               ${extIdInfo}
                               ${producerCodeInfo}
                           </td>
                           <td class="align-middle">${row.currentBlock}</td> <td class="align-middle" style="font-size: 1em; white-space: nowrap; text-align: center;"> <div>${row.arrow} ${formattedDiff} PLN</div>
                               <div style="font-size: 0.9em; color: #555;">(${formattedDiffPercent}%)</div>
                           </td>
                           <td class="align-middle">${row.newBlock}</td> <td class="align-middle" style="white-space: normal; text-align: center;"> ${row.effectDetails}
                           </td>
                           <td class="align-middle text-center"> <button class="remove-change-btn"
                                       data-product-id="${row.productId}"
                                       title="Usuń tę zmianę z symulacji"
                                       style="background: none; border: none; padding: 5px; display: inline-flex; align-items: center; justify-content: center; cursor: pointer;">
                                   <i class="fa fa-trash" style="font-size: 19px; color: #555;"></i> </button>
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

    const bestScoreBtn = document.getElementById("bestScoreButton");
    if (bestScoreBtn) {
        bestScoreBtn.addEventListener("click", function () {
            const tableBody = document.getElementById("simulationTbody");
            if (!tableBody || originalRowsData.length === 0) return;

            let sorted = sortRowsByScoreDesc([...originalRowsData]);
            renderRows(sorted);
        });
    }

    function showExportDisclaimer(type) {
        pendingExportType = type;
        const disclaimerModal = document.getElementById("exportDisclaimerModal");
        if (disclaimerModal) {

            const extendedCheckbox = document.getElementById("extendedExportCheckbox");
            if (extendedCheckbox) extendedCheckbox.checked = false;

            disclaimerModal.style.display = 'block';
            disclaimerModal.classList.add('show');

        }
    }

    function closeExportDisclaimer() {
        pendingExportType = null;
        const disclaimerModal = document.getElementById("exportDisclaimerModal");
        if (disclaimerModal) {
            disclaimerModal.style.display = 'none';
            disclaimerModal.classList.remove('show');
        }
    }

    const disclaimerConfirmButton = document.getElementById("disclaimerConfirmButton");
    if (disclaimerConfirmButton) {
        disclaimerConfirmButton.addEventListener("click", function () {
            const isExtendedExport = document.getElementById("extendedExportCheckbox")?.checked === true;
            closeExportDisclaimer();

            if (pendingExportType === "csv") {
                exportToCsv(isExtendedExport);
            } else if (pendingExportType === "excel") {
                exportToExcelXLSX(isExtendedExport);
            }

        });
    }

    function exportToCsv(isExtended = false) {
        let csvContent = "";
        let fileName = "";
        const dateStr = getCurrentDateString();

        if (isExtended) {

            const baseHeaders = ["ID", "EAN", "KOD", "Nazwa produktu", "Obecna poz. Google", "Oferty Google", "Obecna poz. Ceneo", "Oferty Ceneo", "Obecna cena oferty"];
            const shippingHeaders = ["Obecny koszt wysyłki", "Obecna cena z wysyłką"];
            const marginHeadersCurrent = ["Obecny narzut (%)", "Obecny narzut (PLN)"];
            const newPriceHeader = ["Nowa cena oferty"];
            const newShippingHeaders = ["Nowy koszt wysyłki", "Nowa cena z wysyłką"];
            const marginHeadersNew = ["Nowy narzut (%)", "Nowy narzut (PLN)"];
            const newRankingHeaders = ["Nowa poz. Google", "Nowa liczba ofert Google", "Nowa poz. Ceneo", "Nowa liczba ofert Ceneo"];

            let headers = [...baseHeaders];
            if (usePriceWithDeliverySetting) headers.push(...shippingHeaders);
            headers.push(...marginHeadersCurrent, ...newPriceHeader);
            if (usePriceWithDeliverySetting) headers.push(...newShippingHeaders);
            headers.push(...marginHeadersNew, ...newRankingHeaders);

            csvContent = "\uFEFF" + headers.map(h => `"${h}"`).join(";") + "\n";

            originalRowsData.forEach(row => {

                const formatCsvPrice = (val) => (typeof val === 'number') ? formatPricePL(val, false) : '';
                const formatCsvPercent = (val) => (typeof val === 'number') ? val.toFixed(2).replace('.', ',') : '';
                const formatCsvRank = (val) => (val !== null && val !== undefined) ? String(val) : '';

                let values = [
                    `="${String(row.externalId || '')}"`,
                    `="${String(row.ean || '')}"`,
                    `="${String(row.producerCode || '')}"`,
                    `"${row.productName.replace(/"/g, '""')}"`, // Escape double quotes in name
                    `"${formatCsvRank(row.currentGoogleRanking)}"`,
                    `"${formatCsvRank(row.totalGoogleOffers)}"`,
                    `"${formatCsvRank(row.currentCeneoRanking)}"`,
                    `"${formatCsvRank(row.totalCeneoOffers)}"`,
                    `"${formatCsvPrice(row.baseCurrentPrice)}"`
                ];

                if (usePriceWithDeliverySetting) {
                    values.push(`"${formatCsvPrice(row.ourStoreShippingCost)}"`, `"${formatCsvPrice(row.effectiveCurrentPrice)}"`);
                }

                values.push(`"${formatCsvPercent(row.currentMargin)}"`, `"${formatCsvPrice(row.currentMarginValue)}"`, `"${formatCsvPrice(row.baseNewPrice)}"`);

                if (usePriceWithDeliverySetting) {
                    values.push(`"${formatCsvPrice(row.ourStoreShippingCost)}"`, `"${formatCsvPrice(row.effectiveNewPrice)}"`);
                }

                values.push(
                    `"${formatCsvPercent(row.newMargin)}"`,
                    `"${formatCsvPrice(row.newMarginValue)}"`,
                    `"${formatCsvRank(row.newGoogleRanking)}"`,
                    `"${formatCsvRank(row.totalGoogleOffers)}"`,
                    `"${formatCsvRank(row.newCeneoRanking)}"`,
                    `"${formatCsvRank(row.totalCeneoOffers)}"`
                );

                csvContent += values.join(";") + "\n";
            });
            fileName = `PriceSafari-${dateStr}-${currentOurStoreName}-rozszerzony.csv`;

        } else {
            csvContent = "\uFEFF" + "ID;EAN;KOD;CENA\n";
            originalRowsData.forEach(row => {

                const priceString = formatPricePL(row.baseNewPrice, false);
                const eanText = row.ean ? `="${String(row.ean)}"` : "";
                const producerCodeText = row.producerCode ? `="${String(row.producerCode)}"` : "";
                const extIdText = row.externalId ? `="${String(row.externalId)}"` : "";
                csvContent += `${extIdText};${eanText};${producerCodeText};"${priceString}"\n`;
            });
            fileName = `PriceSafari-${dateStr}-${currentOurStoreName}-bazowa.csv`;
        }

        const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
        const link = document.createElement("a");
        link.href = URL.createObjectURL(blob);
        link.download = fileName;
        link.style.display = 'none';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        setTimeout(() => {
            URL.revokeObjectURL(link.href);
        }, 1000);
    }

    async function exportToExcelXLSX(isExtended = false) {
        if (typeof ExcelJS === 'undefined') {
            console.error("Biblioteka ExcelJS nie jest załadowana.");
            alert("Wystąpił błąd: Biblioteka eksportu do Excela nie jest dostępna.");
            return;
        }

        const workbook = new ExcelJS.Workbook();
        const worksheet = workbook.addWorksheet("Zmiany Cen");
        const dateStr = getCurrentDateString();
        let fileName = "";

        const plNumberFormat = '#,##0.00';

        if (isExtended) {
            let columns = [
                { header: "ID", key: "externalId", width: 18, style: { numFmt: "@" } },
                { header: "EAN", key: "ean", width: 18, style: { numFmt: "@" } },
                { header: "KOD", key: "producerCode", width: 20, style: { numFmt: "@" } },
                { header: "Nazwa produktu", key: "productName", width: 40 },
                { header: "Obecna poz. Google", key: "currentGoogleRanking", width: 20 },
                { header: "Oferty Google", key: "totalGoogleOffers", width: 15, style: { numFmt: '0' } },
                { header: "Obecna poz. Ceneo", key: "currentCeneoRanking", width: 20 },
                { header: "Oferty Ceneo", key: "totalCeneoOffers", width: 15, style: { numFmt: '0' } },
                { header: "Obecna cena oferty", key: "baseCurrentPrice", width: 20, style: { numFmt: plNumberFormat } }
            ];

            if (usePriceWithDeliverySetting) {
                columns.push(
                    { header: "Obecny koszt wysyłki", key: "ourStoreShippingCost", width: 22, style: { numFmt: plNumberFormat } },
                    { header: "Obecna cena z wysyłką", key: "effectiveCurrentPrice", width: 25, style: { numFmt: plNumberFormat } }
                );
            }

            columns.push(
                { header: "Obecny narzut (%)", key: "currentMargin", width: 18, style: { numFmt: plNumberFormat } },
                { header: "Obecny narzut (PLN)", key: "currentMarginValue", width: 18, style: { numFmt: plNumberFormat } },
                { header: "Nowa cena oferty", key: "baseNewPrice", width: 20, style: { numFmt: plNumberFormat } }
            );

            if (usePriceWithDeliverySetting) {
                columns.push(
                    { header: "Nowy koszt wysyłki", key: "newShippingCost", width: 22, style: { numFmt: plNumberFormat } },
                    { header: "Nowa cena z wysyłką", key: "effectiveNewPrice", width: 25, style: { numFmt: plNumberFormat } }
                );
            }

            columns.push(
                { header: "Nowy narzut (%)", key: "newMargin", width: 18, style: { numFmt: plNumberFormat } },
                { header: "Nowy narzut (PLN)", key: "newMarginValue", width: 18, style: { numFmt: plNumberFormat } },
                { header: "Nowa poz. Google", key: "newGoogleRanking", width: 20 },
                { header: "Nowa liczba ofert Google", key: "newTotalGoogleOffers", width: 25, style: { numFmt: '0' } },
                { header: "Nowa poz. Ceneo", key: "newCeneoRanking", width: 20 },
                { header: "Nowa liczba ofert Ceneo", key: "newTotalCeneoOffers", width: 25, style: { numFmt: '0' } }
            );
            worksheet.columns = columns;

            originalRowsData.forEach(row => {

                const safeParseFloat = (val) => {
                    const num = parseFloat(val);
                    return isNaN(num) ? null : num;
                };

                const safeParseInt = (val) => {
                    const num = parseInt(val, 10);
                    return isNaN(num) ? null : num;
                };

                let rowData = {
                    externalId: String(row.externalId || ''),
                    ean: String(row.ean || ''),
                    producerCode: String(row.producerCode || ''),
                    productName: row.productName,

                    currentGoogleRanking: row.currentGoogleRanking !== null ? String(row.currentGoogleRanking) : '',
                    totalGoogleOffers: safeParseInt(row.totalGoogleOffers),
                    currentCeneoRanking: row.currentCeneoRanking !== null ? String(row.currentCeneoRanking) : '',
                    totalCeneoOffers: safeParseInt(row.totalCeneoOffers),

                    baseCurrentPrice: safeParseFloat(row.baseCurrentPrice),
                    currentMargin: safeParseFloat(row.currentMargin),
                    currentMarginValue: safeParseFloat(row.currentMarginValue),
                    baseNewPrice: safeParseFloat(row.baseNewPrice),
                    newMargin: safeParseFloat(row.newMargin),
                    newMarginValue: safeParseFloat(row.newMarginValue),

                    newGoogleRanking: row.newGoogleRanking !== null ? String(row.newGoogleRanking) : '',
                    newTotalGoogleOffers: safeParseInt(row.totalGoogleOffers),
                    newCeneoRanking: row.newCeneoRanking !== null ? String(row.newCeneoRanking) : '',
                    newTotalCeneoOffers: safeParseInt(row.totalCeneoOffers)
                };

                if (usePriceWithDeliverySetting) {
                    rowData.ourStoreShippingCost = safeParseFloat(row.ourStoreShippingCost);
                    rowData.effectiveCurrentPrice = safeParseFloat(row.effectiveCurrentPrice);
                    rowData.newShippingCost = safeParseFloat(row.ourStoreShippingCost);
                    rowData.effectiveNewPrice = safeParseFloat(row.effectiveNewPrice);
                }

                worksheet.addRow(rowData);
            });
            fileName = `PriceSafari-${dateStr}-${currentOurStoreName}-rozszerzony.xlsx`;

        } else {
            worksheet.columns = [
                { header: "ID", key: "externalId", width: 18, style: { numFmt: "@" } },
                { header: "EAN", key: "ean", width: 18, style: { numFmt: "@" } },
                { header: "KOD", key: "producerCode", width: 20, style: { numFmt: "@" } },
                { header: "CENA", key: "newPrice", width: 15, style: { numFmt: plNumberFormat } }
            ];
            originalRowsData.forEach(row => {
                const safeParseFloat = (val) => {
                    const num = parseFloat(val);
                    return isNaN(num) ? null : num;
                };
                worksheet.addRow({
                    externalId: String(row.externalId || ""),
                    ean: String(row.ean || ""),
                    producerCode: String(row.producerCode || ""),
                    newPrice: safeParseFloat(row.baseNewPrice)
                });
            });
            fileName = `PriceSafari-${dateStr}-${currentOurStoreName}-bazowa.xlsx`;
        }

        const buffer = await workbook.xlsx.writeBuffer();
        const blob = new Blob([buffer], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
        const link = document.createElement("a");
        link.href = URL.createObjectURL(blob);
        link.download = fileName;
        link.style.display = 'none';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        setTimeout(() => {
            URL.revokeObjectURL(link.href);
        }, 1000);
    }

    var simulateButton = document.getElementById("simulateButton");
    if (simulateButton) {
        simulateButton.addEventListener("click", function () {
            openSimulationModal();
        });
    }

    const exportToCsvButton = document.getElementById("exportToCsvButton");
    if (exportToCsvButton) {
        exportToCsvButton.addEventListener("click", function () {

            if (originalRowsData.length > 0 || selectedPriceChanges.length > 0) {
                showExportDisclaimer("csv");
            } else {
                alert("Brak danych do wyeksportowania.");
            }
        });
    }

    const exportToExcelButton = document.getElementById("exportNewPriceToExcelButton");
    if (exportToExcelButton) {
        exportToExcelButton.addEventListener("click", function () {
            if (typeof ExcelJS === 'undefined') {
                console.error("Biblioteka ExcelJS nie jest załadowana.");
                alert("Wystąpił błąd: Biblioteka eksportu do Excela nie jest dostępna.");
                return;
            }

            if (originalRowsData.length > 0 || selectedPriceChanges.length > 0) {
                showExportDisclaimer("excel");
            } else {
                alert("Brak danych do wyeksportowania.");
            }
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
            } else {
                console.warn("Funkcja refreshPriceBoxStates nie została znaleziona do odświeżenia widoku.");
            }
        });
    });

    var closeDisclaimerModalButtons = document.querySelectorAll('#exportDisclaimerModal .close, #exportDisclaimerModal [data-dismiss="modal"]');
    closeDisclaimerModalButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            closeExportDisclaimer();
        });
    });

    window.addEventListener('click', function (event) {
        const disclaimerModal = document.getElementById("exportDisclaimerModal");
        if (event.target == disclaimerModal) {
            closeExportDisclaimer();
        }
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

    updatePriceChangeSummary();

}); 