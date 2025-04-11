//document.addEventListener("DOMContentLoaded", function () {
  
//    const localStorageKey = 'selectedPriceChanges_' + storeId;
//    var selectedPriceChanges = [];
//    const storedChanges = localStorage.getItem(localStorageKey);
//    if (storedChanges) {
//        try {
//            selectedPriceChanges = JSON.parse(storedChanges);
//        } catch (err) {
          
//        }
//    }

    
//    let pendingExportType = null;

//    let currentOurStoreName = storeId || "Sklep";

//    let eanMapGlobal = {};

//    function getCurrentDateString() {
//        const d = new Date();
//        return d.toISOString().split('T')[0];
//    }

//    function updatePriceChangeSummary() {
//        var increasedCount = selectedPriceChanges.filter(function (item) {
//            return item.newPrice > item.currentPrice;
//        }).length;
//        var decreasedCount = selectedPriceChanges.filter(function (item) {
//            return item.newPrice < item.currentPrice;
//        }).length;
//        var totalCount = selectedPriceChanges.length;

//        var summaryText = document.getElementById("summaryText");
//        if (summaryText) {
//            summaryText.innerHTML =
//                "<div class='price-change-up' style='display: inline-block;'><span style='color: red;'>▲</span> " + increasedCount + "</div>" +
//                "<div class='price-change-down' style='display: inline-block;'><span style='color: green;'>▼</span> " + decreasedCount + "</div>";
//        }

//        var simulateButton = document.getElementById("simulateButton");
//        if (simulateButton) {
//            simulateButton.disabled = (totalCount === 0);
//            simulateButton.style.opacity = (totalCount === 0 ? '0.5' : '1');
//        }
//    }

  
//    function savePriceChanges() {
//        localStorage.setItem(localStorageKey, JSON.stringify(selectedPriceChanges));
//    }

    
//    function clearPriceChanges() {
//        selectedPriceChanges = [];
//        savePriceChanges();
//        updatePriceChangeSummary();

      
//        const tableContainer = document.getElementById("simulationModalBody");
//        if (tableContainer) {
//            tableContainer.innerHTML = "";
//        }
//    }

//    const clearChangesButton = document.getElementById("clearChangesButton");
//    if (clearChangesButton) {
//        clearChangesButton.addEventListener("click", function () {
//            clearPriceChanges();
//        });
//    }

  
//    document.addEventListener('priceBoxChange', function (event) {
//        const { productId, productName, currentPrice, newPrice } = event.detail;
    

//        var existingIndex = selectedPriceChanges.findIndex(function (item) {
//            return item.productId === productId;
//        });
//        if (existingIndex > -1) {
//            selectedPriceChanges[existingIndex] = { productId, productName, currentPrice, newPrice };
//        } else {
//            selectedPriceChanges.push({ productId, productName, currentPrice, newPrice });
//        }
//        savePriceChanges();
//        updatePriceChangeSummary();
//    });


//    document.addEventListener('priceBoxChangeRemove', function (event) {
//        const { productId } = event.detail;
      

//        selectedPriceChanges = selectedPriceChanges.filter(function (item) {
//            return parseInt(item.productId) !== parseInt(productId);
//        });
//        savePriceChanges();
//        updatePriceChangeSummary();
//    });

 
//    function openSimulationModal() {
//        showLoading();
//        var simulationData = selectedPriceChanges.map(function (item) {
//            return {
//                ProductId: item.productId,
//                CurrentPrice: item.currentPrice,
//                NewPrice: item.newPrice,
//                StoreId: storeId  
//            };
//        });

//        fetch('/PriceHistory/GetPriceChangeDetails?productIds=' +
//            encodeURIComponent(JSON.stringify(selectedPriceChanges.map(function (item) { return item.productId; })))
//        )
//            .then(function (response) {
//                return response.json();
//            })
//            .then(function (productDetails) {
    
//                var hasImage = productDetails.some(function (prod) {
//                    return prod.imageUrl && prod.imageUrl.trim() !== "";
//                });

             
//                fetch('/PriceHistory/SimulatePriceChange', {
//                    method: 'POST',
//                    headers: { 'Content-Type': 'application/json' },
//                    body: JSON.stringify(simulationData)
//                })
//                    .then(function (response) {
//                        return response.json();
//                    })
//                    .then(function (data) {
                    
//                        if (data.ourStoreName) {
//                            currentOurStoreName = data.ourStoreName;
//                        }

//                        var simulationResults = data.simulationResults || [];
//                        // Budujemy mapy dla EAN i External ID
//                        eanMapGlobal = {};
//                        externalIdMapGlobal = {};
//                        simulationResults.forEach(sim => {
//                            const pid = parseInt(sim.productId);
//                            const eanVal = sim.ean ? String(sim.ean) : "";
//                            eanMapGlobal[pid] = eanVal;
//                            const extIdVal = sim.externalId ? String(sim.externalId) : "";
//                            externalIdMapGlobal[pid] = extIdVal;
//                        });

                  
//                        var modalContent = '<table class="table-orders"><thead><tr>';
//                        if (hasImage) {
//                            modalContent += '<th>Produkt</th>';
//                        }
//                        modalContent += '<th></th>';  
//                        modalContent += '<th>Obecne</th>';
//                        modalContent += '<th>Zmiana</th>';
//                        modalContent += '<th>Po zmianie</th>';
//                        modalContent += '<th>Usuń</th>';
//                        modalContent += '</tr></thead><tbody>';

//                        selectedPriceChanges.forEach(function (item) {
                        
//                            var prodDetail = productDetails.find(function (x) {
//                                return parseInt(x.productId) === parseInt(item.productId);
//                            });
//                            var name = prodDetail ? prodDetail.productName : item.productName;
//                            var imageUrl = prodDetail ? prodDetail.imageUrl : "";

                       
//                            var simResult = simulationResults.find(function (x) {
//                                return parseInt(x.productId) === parseInt(item.productId);
//                            });

                        
//                            let ean = "";
//                            if (simResult && simResult.ean) {
//                                ean = String(simResult.ean);
//                            } else if (prodDetail && prodDetail.ean) {
//                                ean = String(prodDetail.ean);
//                            }

                           
//                            let externalId = "";
//                            if (simResult && simResult.externalId) {
//                                externalId = String(simResult.externalId);
//                            } else if (prodDetail && prodDetail.externalId) {
//                                externalId = String(prodDetail.externalId);
//                            }

                         

//                            var diff = item.newPrice - item.currentPrice;
//                            var arrow = diff > 0
//                                ? '<span style="color: red;">▲</span>'
//                                : diff < 0
//                                    ? '<span style="color: green;">▼</span>'
//                                    : '<span style="color: gray;">●</span>';

                        
//                            var googleDisplay = "";
//                            if (simResult && simResult.totalGoogleOffers) {
//                                googleDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
//                                    + ' Pozycja cenowa | '
//                                    + '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:16px; height:16px;" /> '
//                                    + simResult.currentGoogleRanking + ' / ' + simResult.totalGoogleOffers
//                                    + '</div>';
//                            }
//                            var ceneoDisplay = "";
//                            if (simResult && simResult.totalCeneoOffers) {
//                                ceneoDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
//                                    + ' Pozycja cenowa | '
//                                    + '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:16px; height:16px;" /> '
//                                    + simResult.currentCeneoRanking + ' / ' + simResult.totalCeneoOffers
//                                    + '</div>';
//                            }

                          
//                            var googleNewDisplay = "";
//                            if (simResult && simResult.totalGoogleOffers) {
//                                googleNewDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
//                                    + ' Pozycja cenowa | '
//                                    + '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:16px; height:16px;" /> '
//                                    + simResult.newGoogleRanking + ' / ' + simResult.totalGoogleOffers
//                                    + '</div>';
//                            }
//                            var ceneoNewDisplay = "";
//                            if (simResult && simResult.totalCeneoOffers) {
//                                ceneoNewDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
//                                    + ' Pozycja cenowa | '
//                                    + '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:16px; height:16px;" /> '
//                                    + simResult.newCeneoRanking + ' / ' + simResult.totalCeneoOffers
//                                    + '</div>';
//                            }

                           
//                            let currentBlock = '<div class="price-info-box">';
//                            currentBlock += '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
//                                + 'Cena produktu | ' + item.currentPrice.toFixed(2) + ' PLN</div>';
//                            if (googleDisplay) currentBlock += googleDisplay;
//                            if (ceneoDisplay) currentBlock += ceneoDisplay;

//                            if (simResult && simResult.currentMargin != null && simResult.currentMarginValue != null) {
//                                let currMarginValue = parseFloat(simResult.currentMarginValue).toFixed(2);
//                                let currMarginPercentage = parseFloat(simResult.currentMargin).toFixed(2);
//                                let currSign = parseFloat(simResult.currentMarginValue) >= 0 ? "+" : "";
//                                let currClass = parseFloat(simResult.currentMarginValue) >= 0
//                                    ? "priceBox-diff-margin"
//                                    : "priceBox-diff-margin-minus";

//                                currentBlock += `
//                                <div class="price-info-item">
//                                    <div class="price-box-diff-margin ${currClass}">
//                                        <p>Marża: ${currSign}${currMarginValue} PLN (${currSign}${currMarginPercentage}%)</p>
//                                    </div>
//                                </div>`;
//                            }
//                            currentBlock += '</div>';

                      
//                            let newBlock = '<div class="price-info-box">';
//                            newBlock += '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
//                                + 'Cena produktu | ' + item.newPrice.toFixed(2) + ' PLN</div>';
//                            if (googleNewDisplay) newBlock += googleNewDisplay;
//                            if (ceneoNewDisplay) newBlock += ceneoNewDisplay;

                         
//                            if (simResult && simResult.newMargin != null && simResult.newMarginValue != null) {
//                                let newMarginValue = parseFloat(simResult.newMarginValue).toFixed(2);
//                                let newMarginPercentage = parseFloat(simResult.newMargin).toFixed(2);
//                                let newSign = parseFloat(simResult.newMarginValue) >= 0 ? "+" : "";
//                                let newClass = parseFloat(simResult.newMarginValue) >= 0
//                                    ? "priceBox-diff-margin"
//                                    : "priceBox-diff-margin-minus";

//                                newBlock += `
//                                <div class="price-info-item">
//                                    <div class="price-box-diff-margin ${newClass}">
//                                        <p>Marża: ${newSign}${newMarginValue} PLN (${newSign}${newMarginPercentage}%)</p>
//                                    </div>
//                                </div>`;
//                            }
//                            newBlock += '</div>';

//                            modalContent += '<tr>';

//                            if (hasImage) {
//                                if (imageUrl && imageUrl.trim() !== "") {
//                                    modalContent += `
//                                    <td>
//                                        <div class="price-info-item" style="padding:4px;">
//                                            <img
//                                                data-src="${imageUrl}"
//                                                alt="${name}"
//                                                class="lazy-load loaded"
//                                                style="width: 114px; height: 114px; margin-right: 5px; margin-left: 5px; background-color: #fff; border: 1px solid #e3e3e3; border-radius: 4px; padding: 10px; display: block;"
//                                                src="${imageUrl}"
//                                            >
//                                        </div>
//                                    </td>`;
//                                } else {
//                                    modalContent += '<td></td>';
//                                }
//                            }

//                            modalContent += '<td>' +
//                                '<div class="price-info-item" style="font-size:125%;">' + name + '</div>' +
//                                (ean ? '<div class="price-info-item">EAN: ' + ean + '</div>' : '') +
//                                (externalId ? '<div class="price-info-item">ID: ' + externalId + '</div>' : '') +
//                                '</td>';

                       
//                            modalContent += '<td>' + currentBlock + '</td>';

                          
//                            modalContent += '<td style="font-size:16px; white-space: nowrap;">' +
//                                arrow + ' ' + Math.abs(diff).toFixed(2) + ' PLN</td>';

                          
//                            modalContent += '<td>' + newBlock + '</td>';

                        
//                            modalContent += '<td>' +
//                                '<button class="remove-change-btn" data-product-id="' + item.productId + '" style="background: none; border: none; padding: 5px; display: flex; align-items: center; margin-left:8px; justify-content: center;">' +
//                                '<i class="fa fa-trash" style="font-size: 19px; color: red;"></i>' +
//                                '</button>' +
//                                '</td>';

//                            modalContent += '</tr>';
//                        });

//                        modalContent += '</tbody></table>';

//                        var tableContainer = document.getElementById("simulationModalBody");
//                        if (tableContainer) {
//                            tableContainer.innerHTML = modalContent;
//                        }

                     
//                        document.querySelectorAll('.remove-change-btn').forEach(function (btn) {
//                            btn.addEventListener('click', function (e) {
//                                e.stopPropagation();
//                                var prodId = this.getAttribute('data-product-id');
//                                const removeEvent = new CustomEvent('priceBoxChangeRemove', {
//                                    detail: { productId: prodId }
//                                });
//                                document.dispatchEvent(removeEvent);
//                                openSimulationModal();
//                            });
//                        });

                       
//                        var simulationModal = document.getElementById("simulationModal");
//                        simulationModal.style.display = 'block';
//                        simulationModal.classList.add('show');
//                    })
//                    .catch(function (err) {
                     
//                    });
//            })
//            .catch(function (err) {
//                console.error("Błąd pobierania danych produktu:", err);
//            })
//            .finally(function () {
//                hideLoading();
//            });
//    }


//    function showExportDisclaimer(type) {
//        pendingExportType = type;
//        const disclaimerModal = document.getElementById("exportDisclaimerModal");
//        disclaimerModal.style.display = 'block';
//        disclaimerModal.classList.add('show');
//    }

//    function closeExportDisclaimer() {
//        const disclaimerModal = document.getElementById("exportDisclaimerModal");
//        disclaimerModal.style.display = 'none';
//        disclaimerModal.classList.remove('show');
//    }

//    const disclaimerConfirmButton = document.getElementById("disclaimerConfirmButton");
//    if (disclaimerConfirmButton) {
//        disclaimerConfirmButton.addEventListener("click", function () {
//            closeExportDisclaimer();
//            if (pendingExportType === "csv") {
//                exportToCsv();
//            } else if (pendingExportType === "excel") {
//                exportToExcelXLSX();
//            }
//        });
//    }
//    function exportToCsv() {
        
//        let csvContent = "ID,EAN,Nowa cena\n";
//        selectedPriceChanges.forEach(item => {
//            const pid = parseInt(item.productId);
//            const ean = eanMapGlobal[pid] || "";
//            const extId = externalIdMapGlobal[pid] || "";
//            const extIdText = extId ? `="${extId}"` : "";
//            const eanText = ean ? `="${ean}"` : "";
//            const newPrice = item.newPrice != null ? item.newPrice.toFixed(2).replace('.', ',') : "";
//            csvContent += `${extIdText},${eanText},${newPrice}\n`;
//        });

//        const dateStr = getCurrentDateString();
//        const fileName = `PriceSafari-${dateStr}-${currentOurStoreName}.csv`;

//        const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
//        const link = document.createElement("a");
//        link.href = URL.createObjectURL(blob);
//        link.download = fileName;
//        link.click();

//        setTimeout(() => {
//            URL.revokeObjectURL(link.href);
//        }, 1000);
//    }

//    async function exportToExcelXLSX() {
//        const workbook = new ExcelJS.Workbook();
//        const worksheet = workbook.addWorksheet("Zmiany Cen");

//        // Ustawiamy kolumny w kolejności: ID, EAN, Nowa Cena
//        worksheet.columns = [
//            { header: "ID", key: "externalId", width: 18, style: { numFmt: "@" } },
//            { header: "EAN", key: "ean", width: 18, style: { numFmt: "@" } },
//            { header: "Nowa Cena", key: "newPrice", width: 15, style: { numFmt: '#,##0.00' } }
//        ];

//        selectedPriceChanges.forEach(item => {
//            const pid = parseInt(item.productId);
//            const ean = eanMapGlobal[pid] || "";
//            const extId = externalIdMapGlobal[pid] || "";
//            const eanStr = String(ean);
//            const extIdStr = String(extId);
//            const newPrice = item.newPrice != null ? parseFloat(item.newPrice.toFixed(2)) : null;
//            worksheet.addRow({ externalId: extIdStr, ean: eanStr, newPrice: newPrice });
//        });

//        const dateStr = getCurrentDateString();
//        const fileName = `PriceSafari-${dateStr}-${currentOurStoreName}.xlsx`;

//        const buffer = await workbook.xlsx.writeBuffer();
//        const blob = new Blob([buffer], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
//        const link = document.createElement("a");
//        link.href = URL.createObjectURL(blob);
//        link.download = fileName;
//        link.click();

//        setTimeout(() => {
//            URL.revokeObjectURL(link.href);
//        }, 1000);
//    }


  
//    var simulateButton = document.getElementById("simulateButton");
//    if (simulateButton) {
//        simulateButton.addEventListener("click", function () {
//            openSimulationModal();
//        });
//    }

//    const exportToCsvButton = document.getElementById("exportToCsvButton");
//    if (exportToCsvButton) {
//        exportToCsvButton.addEventListener("click", function () {
//            showExportDisclaimer("csv");
//        });
//    }

//    const exportToExcelButton = document.getElementById("exportNewPriceToExcelButton");
//    if (exportToExcelButton) {
//        exportToExcelButton.addEventListener("click", function () {
//            showExportDisclaimer("excel");
//        });
//    }


//    updatePriceChangeSummary();

  
//    var closeButtons = document.querySelectorAll('#simulationModal .close, #simulationModal [data-dismiss="modal"]');
//    closeButtons.forEach(function (btn) {
//        btn.addEventListener('click', function () {
//            var simulationModal = document.getElementById("simulationModal");
//            simulationModal.style.display = 'none';
//            simulationModal.classList.remove('show');
   
//            loadPrices();
//        });
//    });


//    document.querySelectorAll('#exportDisclaimerModal .close, #exportDisclaimerModal [data-dismiss="modal"]').forEach(function (btn) {
//        btn.addEventListener('click', function () {
//            closeExportDisclaimer();
//        });
//    });

//    function closeExportDisclaimer() {
//        const disclaimerModal = document.getElementById("exportDisclaimerModal");
//        disclaimerModal.style.display = 'none';
//        disclaimerModal.classList.remove('show');
//    }
//});






document.addEventListener("DOMContentLoaded", function () {

    const localStorageKey = 'selectedPriceChanges_' + storeId;
    var selectedPriceChanges = [];
    const storedChanges = localStorage.getItem(localStorageKey);
    if (storedChanges) {
        try {
            selectedPriceChanges = JSON.parse(storedChanges);
        } catch (err) {
            // Błąd parsowania JSON - pomijamy
        }
    }

    let pendingExportType = null;
    let currentOurStoreName = storeId || "Sklep";

    let eanMapGlobal = {};
    let externalIdMapGlobal = {};

    // Tu przechowujemy dane wierszy (oryginalna kolejność)
    let originalRowsData = [];

    // Funkcja licząca datę YYYY-MM-DD
    function getCurrentDateString() {
        const d = new Date();
        return d.toISOString().split('T')[0];
    }

    // Aktualizacja podsumowania zmian
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
                "<div class='price-change-up' style='display: inline-block;'><span style='color: red;'>▲</span> " + increasedCount + "</div>" +
                "<div class='price-change-down' style='display: inline-block;'><span style='color: green;'>▼</span> " + decreasedCount + "</div>";
        }

        var simulateButton = document.getElementById("simulateButton");
        if (simulateButton) {
            simulateButton.disabled = (totalCount === 0);
            simulateButton.style.opacity = (totalCount === 0 ? '0.5' : '1');
        }
    }

    // Zapis do localStorage
    function savePriceChanges() {
        localStorage.setItem(localStorageKey, JSON.stringify(selectedPriceChanges));
    }

    // Czyszczenie zmian
    function clearPriceChanges() {
        selectedPriceChanges = [];
        savePriceChanges();
        updatePriceChangeSummary();
        const tableContainer = document.getElementById("simulationModalBody");
        if (tableContainer) {
            tableContainer.innerHTML = "";
        }
    }

    const clearChangesButton = document.getElementById("clearChangesButton");
    if (clearChangesButton) {
        clearChangesButton.addEventListener("click", function () {
            clearPriceChanges();
        });
    }

    // Obsługa eventów: dodawanie/usuwanie produktów do listy zmian
    document.addEventListener('priceBoxChange', function (event) {
        const { productId, productName, currentPrice, newPrice } = event.detail;
        var existingIndex = selectedPriceChanges.findIndex(function (item) {
            return item.productId === productId;
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
    });

    // Loader (jeśli używasz)
    function showLoading() {
        document.getElementById("loadingOverlay").style.display = "flex";
    }
    function hideLoading() {
        document.getElementById("loadingOverlay").style.display = "none";
    }

    // Parsowanie rankingu "3-23 / 24" => 23
    function parseRankingString(rankStr) {
        if (!rankStr) return null;
        const spaceIndex = rankStr.indexOf(' ');
        let mainPart = (spaceIndex !== -1) ? rankStr.substring(0, spaceIndex) : rankStr;
        const dashIndex = mainPart.indexOf('-');
        if (dashIndex !== -1) {
            let afterDash = mainPart.substring(dashIndex + 1).trim();
            let val = parseFloat(afterDash);
            return isNaN(val) ? null : val;
        }
        let val = parseFloat(mainPart);
        return isNaN(val) ? null : val;
    }

    // Propozycja formuły:
    // score = 100 * ( ( (googleOld - googleNew)/ totalGoogleOffers ) + ((ceneoOld - ceneoNew)/ totalCeneoOffers ) ) / ( ( absDiff / currentPrice ) + 0.001 )
    // => a do tego jeszcze chcemy zwrócić dodatkowe info do wyświetlenia
    function computeOpportunityScore(item, simResult) {
        // item => { currentPrice, newPrice }
        // simResult => { currentGoogleRanking, newGoogleRanking, totalGoogleOffers, ... }

        let googleOld = simResult.currentGoogleRanking ? parseRankingString(String(simResult.currentGoogleRanking)) : null;
        let googleNew = simResult.newGoogleRanking ? parseRankingString(String(simResult.newGoogleRanking)) : null;
        let totalG = simResult.totalGoogleOffers ? parseInt(simResult.totalGoogleOffers, 10) : 0;

        let googleGained = 0;
        if (googleOld !== null && googleNew !== null && totalG > 0) {
            let diff = googleOld - googleNew;
            if (diff > 0) {
                googleGained = diff;
            }
        }
        let googleFrac = totalG > 0 ? (googleGained / totalG) : 0;

        let ceneoOld = simResult.currentCeneoRanking ? parseRankingString(String(simResult.currentCeneoRanking)) : null;
        let ceneoNew = simResult.newCeneoRanking ? parseRankingString(String(simResult.newCeneoRanking)) : null;
        let totalC = simResult.totalCeneoOffers ? parseInt(simResult.totalCeneoOffers, 10) : 0;

        let ceneoGained = 0;
        if (ceneoOld !== null && ceneoNew !== null && totalC > 0) {
            let diff = ceneoOld - ceneoNew;
            if (diff > 0) {
                ceneoGained = diff;
            }
        }
        let ceneoFrac = totalC > 0 ? (ceneoGained / totalC) : 0;

        let sumFrac = googleFrac + ceneoFrac;

        let cost = Math.abs(item.newPrice - item.currentPrice);
        let costFrac = 0;
        if (item.currentPrice > 0) {
            costFrac = cost / item.currentPrice;
        }

        // Sam "score"
        let rawScore = 100 * (sumFrac / (costFrac + 0.001));

        // Zwróćmy obiekt z dodatkowymi informacjami, żeby można je było wyświetlić
        return {
            rawScore,
            cost,             // w PLN
            costFrac,         // w %
            googleGained,
            googleFrac,
            ceneoGained,
            ceneoFrac
        };
    }

    // Budujemy blok "Obecne" / "Po zmianie"
    function buildPriceBlock(price, marginPercent, marginValue, googleRank, googleOffers, ceneoRank, ceneoOffers) {
        let block = '<div class="price-info-box">';
        block += `
            <div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px;">
                Cena produktu | ${price.toFixed(2)} PLN
            </div>`;

        if (googleRank && googleOffers) {
            block += `
            <div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px;">
                Pozycja cenowa | <img src="/images/GoogleShopping.png" alt="Google Icon" style="width:16px; height:16px;" />
                ${googleRank} / ${googleOffers}
            </div>`;
        }
        if (ceneoRank && ceneoOffers) {
            block += `
            <div class="price-info-item" style="padding: 4px 12px; background: #f5f5f5; border: 1px solid #e3e3e3; border-radius: 5px;">
                Pozycja cenowa | <img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:16px; height:16px;" />
                ${ceneoRank} / ${ceneoOffers}
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

    // Otwieramy okno symulacji
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

        fetch('/PriceHistory/GetPriceChangeDetails?productIds=' +
            encodeURIComponent(JSON.stringify(selectedPriceChanges.map(function (item) { return item.productId; })))
        )
            .then(function (response) {
                return response.json();
            })
            .then(function (productDetails) {
                var hasImage = productDetails.some(function (prod) {
                    return prod.imageUrl && prod.imageUrl.trim() !== "";
                });

                return fetch('/PriceHistory/SimulatePriceChange', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(simulationData)
                })
                    .then(function (response) {
                        return response.json();
                    })
                    .then(function (data) {
                        return { productDetails, data, hasImage };
                    });
            })
            .then(function ({ productDetails, data, hasImage }) {
                if (data.ourStoreName) {
                    currentOurStoreName = data.ourStoreName;
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
                tableHtml += '<th>Efekt zmiany (score)</th>';
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

                    // Budujemy bloki
                    let currentBlock = buildPriceBlock(
                        item.currentPrice,
                        simResult && simResult.currentMargin,
                        simResult && simResult.currentMarginValue,
                        simResult && simResult.currentGoogleRanking,
                        simResult && simResult.totalGoogleOffers,
                        simResult && simResult.currentCeneoRanking,
                        simResult && simResult.totalCeneoOffers
                    );
                    let newBlock = buildPriceBlock(
                        item.newPrice,
                        simResult && simResult.newMargin,
                        simResult && simResult.newMarginValue,
                        simResult && simResult.newGoogleRanking,
                        simResult && simResult.totalGoogleOffers,
                        simResult && simResult.newCeneoRanking,
                        simResult && simResult.totalCeneoOffers
                    );

                    // Liczymy naszą "zaawansowaną" formułę
                    let opp = computeOpportunityScore(item, simResult);

                    // Tworzymy wieloliniowy opis, żeby pokazać szczegóły
                    // cost: opp.cost – w PLN
                    // costFrac: opp.costFrac – w skali 0..1 => *100
                    // googleGained => ile pozycji
                    // googleFrac => googleGained / totalOffers
                    // rawScore => final
                    let costPercent = opp.costFrac * 100;
                    let googleFracPerc = opp.googleFrac * 100;
                    let ceneoFracPerc = opp.ceneoFrac * 100;

                    let effectDetails = `
                    <div style="margin-bottom:4px; color: #333;">
                        Zmiana ceny: ${opp.cost.toFixed(4)} PLN (${costPercent.toFixed(3)}%)
                    </div>
                    <div style="margin-bottom:4px; color: #333;">
                        Google: +${opp.googleGained} poz. = ${googleFracPerc.toFixed(2)}% rank
                    </div>
                    <div style="margin-bottom:4px; color: #333;">
                        Ceneo: +${opp.ceneoGained} poz. = ${ceneoFracPerc.toFixed(2)}% rank
                    </div>
                    <div style="margin-top:6px; font-weight:bold; color:#008000;">
                        Score: ${opp.rawScore.toFixed(3)}
                    </div>
                `;

                    rowsData.push({
                        index,
                        hasImage,
                        imageUrl,
                        productName: name,
                        ean,
                        externalId,
                        diff,
                        arrow,
                        currentBlock,
                        newBlock,
                        finalScore: opp.rawScore,  // do sortowania
                        effectDetails,
                        productId: item.productId
                    });
                });

                originalRowsData = [...rowsData];
                renderRows(rowsData);

                var simulationModal = document.getElementById("simulationModal");
                simulationModal.style.display = 'block';
                simulationModal.classList.add('show');
            })
            .catch(function (err) {
                console.error("Błąd pobierania danych produktu / symulacji:", err);
            })
            .finally(function () {
                hideLoading();
            });
    }

    // Renderowanie wierszy w tabeli
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
                    <div class="price-info-item" style="font-size:125%;">${row.productName}</div>
                    ${eanInfo}
                    ${extIdInfo}
                </td>
                <td>${row.currentBlock}</td>
                <td style="font-size:16px; white-space: nowrap;">
                    ${row.arrow} ${Math.abs(row.diff).toFixed(2)} PLN
                </td>
                <td>${row.newBlock}</td>
                <td style="white-space: nowrap;">
                    ${row.effectDetails}
                </td>
                <td>
                    <button class="remove-change-btn"
                        data-product-id="${row.productId}"
                        style="background: none; border: none; padding: 5px; display: flex; align-items: center; margin-left:8px; justify-content: center;">
                        <i class="fa fa-trash" style="font-size: 19px; color: red;"></i>
                    </button>
                </td>
            </tr>`;
        });

        tbody.innerHTML = html;

        // Obsługa usuwania wierszy
        document.querySelectorAll('.remove-change-btn').forEach(btn => {
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                var prodId = this.getAttribute('data-product-id');
                const removeEvent = new CustomEvent('priceBoxChangeRemove', {
                    detail: { productId: prodId }
                });
                document.dispatchEvent(removeEvent);
                openSimulationModal();
            });
        });
    }

    // Przykładowe sortowanie – np. rosnąco po finalScore
    function sortRowsByScoreAsc(rows) {
        rows.sort((a, b) => a.finalScore - b.finalScore);
        return rows;
    }
    // Przykład malejąco (najbardziej opłacalne – najwyższy score – na górze)
    function sortRowsByScoreDesc(rows) {
        rows.sort((a, b) => b.finalScore - a.finalScore);
        return rows;
    }

    // Jeżeli masz przycisk do sortowania
    const bestScoreBtn = document.getElementById("bestScoreButton");
    if (bestScoreBtn) {
        bestScoreBtn.addEventListener("click", function () {
            const tableBody = document.getElementById("simulationTbody");
            if (!tableBody) return;
            let sorted = sortRowsByScoreDesc([...originalRowsData]);
            renderRows(sorted);
        });
    }

    // Eksport CSV/Excel
    function showExportDisclaimer(type) {
        pendingExportType = type;
        const disclaimerModal = document.getElementById("exportDisclaimerModal");
        disclaimerModal.style.display = 'block';
        disclaimerModal.classList.add('show');
    }

    function closeExportDisclaimer() {
        const disclaimerModal = document.getElementById("exportDisclaimerModal");
        disclaimerModal.style.display = 'none';
        disclaimerModal.classList.remove('show');
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
        });
    }

    function exportToCsv() {
        let csvContent = "ID,EAN,Nowa cena\n";
        selectedPriceChanges.forEach(item => {
            const pid = parseInt(item.productId);
            const ean = eanMapGlobal[pid] || "";
            const extId = externalIdMapGlobal[pid] || "";
            const extIdText = extId ? `="${extId}"` : "";
            const eanText = ean ? `="${ean}"` : "";
            const newPrice = item.newPrice != null ? item.newPrice.toFixed(2).replace('.', ',') : "";
            csvContent += `${extIdText},${eanText},${newPrice}\n`;
        });

        const dateStr = getCurrentDateString();
        const fileName = `PriceSafari-${dateStr}-${currentOurStoreName}.csv`;

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
        const worksheet = workbook.addWorksheet("Zmiany Cen");

        worksheet.columns = [
            { header: "ID", key: "externalId", width: 18, style: { numFmt: "@" } },
            { header: "EAN", key: "ean", width: 18, style: { numFmt: "@" } },
            { header: "Nowa Cena", key: "newPrice", width: 15, style: { numFmt: '#,##0.00' } }
        ];

        selectedPriceChanges.forEach(item => {
            const pid = parseInt(item.productId);
            const ean = eanMapGlobal[pid] || "";
            const extId = externalIdMapGlobal[pid] || "";
            const eanStr = String(ean);
            const extIdStr = String(extId);
            const newPrice = item.newPrice != null ? parseFloat(item.newPrice.toFixed(2)) : null;
            worksheet.addRow({ externalId: extIdStr, ean: eanStr, newPrice: newPrice });
        });

        const dateStr = getCurrentDateString();
        const fileName = `PriceSafari-${dateStr}-${currentOurStoreName}.xlsx`;

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

    // Obsługa przycisków: Symuluj, Export CSV/Excel
    var simulateButton = document.getElementById("simulateButton");
    if (simulateButton) {
        simulateButton.addEventListener("click", function () {
            openSimulationModal();
        });
    }

    const exportToCsvButton = document.getElementById("exportToCsvButton");
    if (exportToCsvButton) {
        exportToCsvButton.addEventListener("click", function () {
            showExportDisclaimer("csv");
        });
    }

    const exportToExcelButton = document.getElementById("exportNewPriceToExcelButton");
    if (exportToExcelButton) {
        exportToExcelButton.addEventListener("click", function () {
            showExportDisclaimer("excel");
        });
    }

    updatePriceChangeSummary();

    // Obsługa zamykania modala
    var closeButtons = document.querySelectorAll('#simulationModal .close, #simulationModal [data-dismiss="modal"]');
    closeButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            var simulationModal = document.getElementById("simulationModal");
            simulationModal.style.display = 'none';
            simulationModal.classList.remove('show');
            // Jeśli używasz do przeładowania listy
            loadPrices();
        });
    });

    document.querySelectorAll('#exportDisclaimerModal .close, #exportDisclaimerModal [data-dismiss="modal"]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            closeExportDisclaimer();
        });
    });

    function closeExportDisclaimer() {
        const disclaimerModal = document.getElementById("exportDisclaimerModal");
        disclaimerModal.style.display = 'none';
        disclaimerModal.classList.remove('show');
    }
});
