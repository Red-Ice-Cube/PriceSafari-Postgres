document.addEventListener("DOMContentLoaded", function () {

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
                    background: conic-gradient(${color} 0% ${percentage}%, #fff ${percentage}% 100%);
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
    const storedChanges = localStorage.getItem(localStorageKey);
    if (storedChanges) {
        try {
            selectedPriceChanges = JSON.parse(storedChanges);
        } catch (err) {
            console.error("Błąd parsowania storedChanges z Local Storage:", err);
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

    function updatePriceChangeSummary() {
        var increasedCount = selectedPriceChanges.filter(function (item) {
            return item.newPrice > item.currentPrice;
        }).length;
        var decreasedCount = selectedPriceChanges.filter(function (item) {
            return item.newPrice < item.currentPrice;
        }).length;
        var totalCount = selectedPriceChanges.length;

        var summaryText = document.getElementById("summaryText");
        if (summaryText) {
            summaryText.innerHTML =
                "<div class='price-change-up' style='display: inline-block; margin-right: 10px;'><span style='color: red;'>▲</span> " + increasedCount + "</div>" +
                "<div class='price-change-down' style='display: inline-block;'><span style='color: green;'>▼</span> " + decreasedCount + "</div>";
        }

        var simulateButton = document.getElementById("simulateButton");
        if (simulateButton) {
            simulateButton.disabled = (totalCount === 0);
            simulateButton.style.opacity = (totalCount === 0 ? '0.5' : '1');
        }
    }

    function savePriceChanges() {
        localStorage.setItem(localStorageKey, JSON.stringify(selectedPriceChanges));
    }

    function clearPriceChanges() {
        selectedPriceChanges = [];
        savePriceChanges();
        updatePriceChangeSummary();
        const tableContainer = document.getElementById("simulationModalBody");
        if (tableContainer) {
            tableContainer.innerHTML = '<p style="text-align: center; padding: 20px; font-size: 16px;">Brak produktów do symulacji.</p>';
        }

        const simulationTable = document.getElementById("simulationTable");
        if (simulationTable) simulationTable.remove();

    }

    const clearChangesButton = document.getElementById("clearChangesButton");
    if (clearChangesButton) {
        clearChangesButton.addEventListener("click", function () {
            clearPriceChanges();
        });
    }

    document.addEventListener('priceBoxChange', function (event) {
        const { productId, productName, currentPrice, newPrice } = event.detail;
        var existingIndex = selectedPriceChanges.findIndex(function (item) {
            return parseInt(item.productId) === parseInt(productId);
        });
        if (existingIndex > -1) {
            selectedPriceChanges[existingIndex] = { productId, productName, currentPrice, newPrice };
        } else {
            selectedPriceChanges.push({ productId, productName, currentPrice, newPrice });
        }
        savePriceChanges();
        updatePriceChangeSummary();
    });

    document.addEventListener('priceBoxChangeRemove', function (event) {
        const { productId } = event.detail;
        selectedPriceChanges = selectedPriceChanges.filter(function (item) {
            return parseInt(item.productId) !== parseInt(productId);
        });
        savePriceChanges();
        updatePriceChangeSummary();

        const rowElement = document.querySelector(`#simulationTbody tr button[data-product-id='${productId}']`)?.closest('tr');
        if (rowElement) {
            rowElement.remove();
        }

        originalRowsData = originalRowsData.filter(function (rowItem) {
            return parseInt(rowItem.productId) !== parseInt(productId);
        });

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
        document.getElementById("loadingOverlay").style.display = "flex";
    }
    function hideLoading() {
        document.getElementById("loadingOverlay").style.display = "none";
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
        if (basePriceDiff > 0) basePriceChangeType = 'increase';
        else if (basePriceDiff < 0) basePriceChangeType = 'decrease';

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

            let gainedPos = oldRankData.rank - newRankData.rank;

            if (gainedPos <= 0) return null;

            let jumpFrac = gainedPos * Math.log10(totalOffers + 1);

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

        return {
            cost,
            costFrac,
            googleGained: googleOldData.rank !== null && googleNewData.rank !== null ? googleOldData.rank - googleNewData.rank : 0,
            ceneoGained: ceneoOldData.rank !== null && ceneoNewData.rank !== null ? ceneoOldData.rank - ceneoNewData.rank : 0,
            googleFinalScore,
            ceneoFinalScore,
            basePriceChangeType
        };
    }

    function buildPriceBlock(basePrice, effectivePrice, shippingCost, usePriceWithDeliveryFlag, marginPercent, marginValue, googleRank, googleOffers, ceneoRank, ceneoOffers) {

        const validBasePrice = typeof basePrice === 'number' ? basePrice : null;
        const validEffectivePrice = typeof effectivePrice === 'number' ? effectivePrice : null;
        const validShippingCost = typeof shippingCost === 'number' ? shippingCost : null;

        let block = '<div class="price-info-box">';

        block += `
             <div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px;">
                 Cena oferty | ${validBasePrice !== null ? validBasePrice.toFixed(2) + ' PLN' : '-'}
             </div>`;

        if (usePriceWithDeliveryFlag && validEffectivePrice !== null && validShippingCost !== null) {

            block += `
                 <div class="price-info-item" style="padding: 4px 12px; background: #e5f5e5; border: 1px solid #c3e3c3; border-radius: 5px; margin-top: 5px; margin-bottom: 2px;">
                     Cena z wysyłką | ${validEffectivePrice.toFixed(2)} PLN
                 </div>`;

            block += `
                  <div class="price-info-item" style="padding: 4px 12px; background: #e5f5e5; border: 1px solid #c3e3c3; border-radius: 5px;">
                    Koszt | ${validBasePrice !== null ? validBasePrice.toFixed(2) : '-'} PLN + ${validShippingCost.toFixed(2)} PLN
                  </div>`;
        }

        if (googleRank && googleOffers) {
            const googleOffersText = googleOffers > 0 ? googleOffers : '-';
            block += `
             <div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px;">
                 Poz. cenowa | <img src="/images/GoogleShopping.png" alt="Google Icon" style="width:16px; height:16px; vertical-align: middle;" />
                 ${googleRank} / ${googleOffersText}
             </div>`;
        }
        if (ceneoRank && ceneoOffers) {
            const ceneoOffersText = ceneoOffers > 0 ? ceneoOffers : '-';
            block += `
             <div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px;">
                 Poz. cenowa | <img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:16px; height:16px; vertical-align: middle;" />
                 ${ceneoRank} / ${ceneoOffersText}
             </div>`;
        }

        if (marginPercent != null && marginValue != null) {
            let mv = parseFloat(marginValue).toFixed(2);
            let mp = parseFloat(marginPercent).toFixed(2);
            let sign = parseFloat(marginValue) >= 0 ? "+" : "";
            let cls = parseFloat(marginValue) >= 0
                ? "priceBox-diff-margin"
                : "priceBox-diff-margin-minus";
            block += `
                 <div class="price-info-item">
                     <div class="price-box-diff-margin ${cls}">
                         <p>Marża: ${sign}${mv} PLN (${sign}${mp}%)</p>
                     </div>
                 </div>`;
        }

        block += '</div>';
        return block;
    }

    function openSimulationModal() {
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
                    throw new Error(`HTTP error! status: ${response.status}`);
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
                            throw new Error(`HTTP error! status: ${response.status}`);
                        }
                        return response.json();
                    })
                    .then(data => {
                        return { productDetails, data, hasImage };
                    });
            })
            .then(({ productDetails, data, hasImage }) => {

                usePriceWithDeliverySetting = data.usePriceWithDelivery;

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
                    const pid = parseInt(sim.productId);
                    const eanVal = sim.ean ? String(sim.ean) : "";
                    eanMapGlobal[pid] = eanVal;
                    const extIdVal = sim.externalId ? String(sim.externalId) : "";
                    externalIdMapGlobal[pid] = extIdVal;
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

                let rowsData = [];

                selectedPriceChanges.forEach(function (item, index) {
                    const prodDetail = productDetails.find(x => parseInt(x.productId) === parseInt(item.productId));
                    const name = prodDetail ? prodDetail.productName : item.productName;
                    const imageUrl = prodDetail ? prodDetail.imageUrl : "";

                    const simResult = simulationResults.find(x => parseInt(x.productId) === parseInt(item.productId));

                    let ean = "";
                    if (simResult && simResult.ean) {
                        ean = String(simResult.ean);
                    } else if (prodDetail && prodDetail.ean) {
                        ean = String(prodDetail.ean);
                    }

                    let externalId = "";
                    if (simResult && simResult.externalId) {
                        externalId = String(simResult.externalId);
                    } else if (prodDetail && prodDetail.externalId) {
                        externalId = String(prodDetail.externalId);
                    }

                    let diff = item.newPrice - item.currentPrice;
                    let arrow = diff > 0
                        ? '<span style="color: red;">▲</span>'
                        : diff < 0
                            ? '<span style="color: green;">▼</span>'
                            : '<span style="color: gray;">●</span>';

                    let diffPercent = 0;
                    if (item.currentPrice > 0) {
                        diffPercent = (diff / item.currentPrice) * 100;
                    }

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

                        if (isPriceStepAdjustment) {
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

                    rowsData.push({
                        index,
                        hasImage,
                        imageUrl,
                        productName: name,
                        ean,
                        externalId,
                        diff,
                        diffPercent,
                        arrow,
                        currentBlock,
                        newBlock,
                        finalScore: bestScore,
                        effectDetails,
                        productId: item.productId,
                        scrapId: globalLatestScrapId,

                        baseCurrentPrice: simResult ? simResult.baseCurrentPrice : null,
                        baseNewPrice: simResult ? simResult.baseNewPrice : null,
                        effectiveCurrentPrice: simResult ? simResult.effectiveCurrentPrice : null,
                        effectiveNewPrice: simResult ? simResult.effectiveNewPrice : null,
                        ourStoreShippingCost: simResult ? simResult.ourStoreShippingCost : null

                    });
                });

                originalRowsData = rowsData;

                renderRows(rowsData);

                var simulationModal = document.getElementById("simulationModal");
                if (simulationModal) {
                    simulationModal.style.display = 'block';
                    simulationModal.classList.add('show');
                }

            })
            .catch(function (err) {
                console.error("Błąd pobierania danych produktu / symulacji:", err);

                const tableContainer = document.getElementById("simulationModalBody");
                if (tableContainer) {
                    tableContainer.innerHTML = '<p style="text-align: center; padding: 20px; font-size: 16px; color: red;">Wystąpił błąd podczas ładowania symulacji. Spróbuj ponownie.</p>';
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
                if (row.imageUrl && row.imageUrl.trim() !== "") {
                    imageCell = `
                        <td>
                            <div class="price-info-item" style="padding:4px;">
                                <img
                                    data-src="${row.imageUrl}"
                                    alt="${row.productName}"
                                    class="lazy-load loaded"
                                    style="width: 114px; height: 114px; margin-right: 5px; margin-left: 5px; background-color: #fff; border: 1px solid #e3e3e3; border-radius: 4px; padding: 10px; display: block;"
                                    src="${row.imageUrl}"
                                >
                            </div>
                        </td>`;
                } else {
                    imageCell = "<td></td>";
                }
            }

            let eanInfo = row.ean ? `<div class="price-info-item">EAN: ${row.ean}</div>` : "";
            let extIdInfo = row.externalId ? `<div class="price-info-item">ID: ${row.externalId}</div>` : "";

            html += `<tr>
                 ${imageCell}
                 <td>
                    <a
                      href="/PriceHistory/Details?scrapId=${row.scrapId}&productId=${row.productId}"
                      target="_blank"
                      rel="noopener noreferrer"
                      class="simulationProductTitle"
                    >
                      <div class="price-info-item" style="font-size:125%;">
                        ${row.productName}
                      </div>
                    </a>
                    ${eanInfo}
                    ${extIdInfo}
                </td>
                <td>${row.currentBlock}</td>
                <td style="font-size:16px; white-space: nowrap;">
                    ${row.arrow} ${Math.abs(row.diff).toFixed(2)} PLN
                    (${Math.abs(row.diffPercent).toFixed(2)}%)
                </td>
                <td>${row.newBlock}</td>
                <td style="white-space: nowrap;">
                    ${row.effectDetails}
                </td>
                <td>
                    <button class="remove-change-btn"
                         data-product-id="${row.productId}"
                         style="background: none; border: none; padding: 5px; display: flex; align-items: center; margin-left:8px; justify-content: center; cursor: pointer;">
                        <i class="fa fa-trash" style="font-size: 19px; color: rgb(51, 51, 51);"></i>
                    </button>
                </td>
            </tr>`;
        });

        tbody.innerHTML = html;

        document.querySelectorAll('#simulationTbody .remove-change-btn').forEach(btn => {
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
            disclaimerModal.style.display = 'block';
            disclaimerModal.classList.add('show');
        }
    }

    function closeExportDisclaimer() {
        const disclaimerModal = document.getElementById("exportDisclaimerModal");
        if (disclaimerModal) {
            disclaimerModal.style.display = 'none';
            disclaimerModal.classList.remove('show');
        }
    }

    const disclaimerConfirmButton = document.getElementById("disclaimerConfirmButton");
    if (disclaimerConfirmButton) {
        disclaimerConfirmButton.addEventListener("click", function () {
            closeExportDisclaimer();
            if (pendingExportType === "csv") {
                exportToCsv();
            } else if (pendingExportType === "excel") {
                exportToExcelXLSX();
            }
            pendingExportType = null;
        });
    }
    function exportToCsv() {

        let csvContent = "ID,EAN,CENA\n";

        originalRowsData.forEach(row => {
            const baseNewPrice = (typeof row.baseNewPrice === 'number') ? row.baseNewPrice.toFixed(2).replace('.', ',') : "";

            const eanText = row.ean ? `="${row.ean}"` : "";
            const extIdText = row.externalId ? `="${row.externalId}"` : "";

            csvContent += `${extIdText},${eanText},${baseNewPrice}\n`;
        });

        const dateStr = getCurrentDateString();

        const fileName = `PriceSafari-${dateStr}-${currentOurStoreName}-bazowa.csv`;

        const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
        const link = document.createElement("a");
        link.href = URL.createObjectURL(blob);
        link.download = fileName;
        link.click();

        setTimeout(() => {
            URL.revokeObjectURL(link.href);
        }, 1000);
    }

    async function exportToExcelXLSX() {

        const workbook = new ExcelJS.Workbook();

        const worksheet = workbook.addWorksheet("Zmiany Cen Bazowych");

        worksheet.columns = [
            { header: "ID", key: "externalId", width: 18, style: { numFmt: "@" } },
            { header: "EAN", key: "ean", width: 18, style: { numFmt: "@" } },

            { header: "CENA", key: "newPrice", width: 15, style: { numFmt: '#,##0.00' } }
        ];

        originalRowsData.forEach(row => {
            const baseNewPrice = (typeof row.baseNewPrice === 'number') ? parseFloat(row.baseNewPrice.toFixed(2)) : null;

            const eanStr = String(row.ean || "");
            const extIdStr = String(row.externalId || "");

            worksheet.addRow({ externalId: extIdStr, ean: eanStr, newPrice: baseNewPrice });
        });

        const dateStr = getCurrentDateString();

        const fileName = `PriceSafari-${dateStr}-${currentOurStoreName}-bazowa.xlsx`;

        const buffer = await workbook.xlsx.writeBuffer();
        const blob = new Blob([buffer], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
        const link = document.createElement("a");
        link.href = URL.createObjectURL(blob);
        link.download = fileName;
        link.click();

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
            if (selectedPriceChanges.length > 0) showExportDisclaimer("csv");
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
            if (selectedPriceChanges.length > 0) showExportDisclaimer("excel");
        });
    }

    updatePriceChangeSummary();

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
            } else if (typeof window.loadPrices === 'function') {
                console.warn("Funkcja refreshPriceBoxStates nie została znaleziona. Rozważam fallback do loadPrices().");
                window.loadPrices();
            } else {
                console.error("Brak funkcji odświeżającej stany cen na liście.");
            }
        });
    });

    var closeDisclaimerModalButtons = document.querySelectorAll('#exportDisclaimerModal .close, #exportDisclaimerModal [data-dismiss="modal"]');
    closeDisclaimerModalButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            closeExportDisclaimer();
        });
    });

});