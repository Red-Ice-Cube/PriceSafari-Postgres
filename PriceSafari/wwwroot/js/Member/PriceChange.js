document.addEventListener("DOMContentLoaded", function () {
    // Używamy unikalnego klucza dla danego sklepu
    const localStorageKey = 'selectedPriceChanges_' + storeId;
    var selectedPriceChanges = [];
    const storedChanges = localStorage.getItem(localStorageKey);
    if (storedChanges) {
        try {
            selectedPriceChanges = JSON.parse(storedChanges);
        } catch (err) {
          
        }
    }

    
    let pendingExportType = null;

    let currentOurStoreName = storeId || "Sklep";

    let eanMapGlobal = {};

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
                "<div class='price-change-up' style='display: inline-block;'><span style='color: red;'>▲</span> " + increasedCount + "</div>" +
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
            tableContainer.innerHTML = "";
        }
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

 
    function openSimulationModal() {
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

             
                fetch('/PriceHistory/SimulatePriceChange', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(simulationData)
                })
                    .then(function (response) {
                        return response.json();
                    })
                    .then(function (data) {
                    
                        if (data.ourStoreName) {
                            currentOurStoreName = data.ourStoreName;
                        }

                        var simulationResults = data.simulationResults || [];
              
                        eanMapGlobal = {};
                        simulationResults.forEach(sim => {
                            const pid = parseInt(sim.productId);
                            const eanVal = sim.ean ? String(sim.ean) : "";
                            eanMapGlobal[pid] = eanVal;
                      
                        });

                  
                        var modalContent = '<table class="table-orders"><thead><tr>';
                        if (hasImage) {
                            modalContent += '<th>Produkt</th>';
                        }
                        modalContent += '<th></th>';  
                        modalContent += '<th>Obecne</th>';
                        modalContent += '<th>Zmiana</th>';
                        modalContent += '<th>Po zmianie</th>';
                        modalContent += '<th>Usuń</th>';
                        modalContent += '</tr></thead><tbody>';

                        selectedPriceChanges.forEach(function (item) {
                        
                            var prodDetail = productDetails.find(function (x) {
                                return parseInt(x.productId) === parseInt(item.productId);
                            });
                            var name = prodDetail ? prodDetail.productName : item.productName;
                            var imageUrl = prodDetail ? prodDetail.imageUrl : "";

                       
                            var simResult = simulationResults.find(function (x) {
                                return parseInt(x.productId) === parseInt(item.productId);
                            });

                        
                            let ean = "";
                            if (simResult && simResult.ean) {
                                ean = String(simResult.ean);
                            } else if (prodDetail && prodDetail.ean) {
                                ean = String(prodDetail.ean);
                            }

                         
                         

                            var diff = item.newPrice - item.currentPrice;
                            var arrow = diff > 0
                                ? '<span style="color: red;">▲</span>'
                                : diff < 0
                                    ? '<span style="color: green;">▼</span>'
                                    : '<span style="color: gray;">●</span>';

                        
                            var googleDisplay = "";
                            if (simResult && simResult.totalGoogleOffers) {
                                googleDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                                    + ' Pozycja cenowa | '
                                    + '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:16px; height:16px;" /> '
                                    + simResult.currentGoogleRanking + ' / ' + simResult.totalGoogleOffers
                                    + '</div>';
                            }
                            var ceneoDisplay = "";
                            if (simResult && simResult.totalCeneoOffers) {
                                ceneoDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                                    + ' Pozycja cenowa | '
                                    + '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:16px; height:16px;" /> '
                                    + simResult.currentCeneoRanking + ' / ' + simResult.totalCeneoOffers
                                    + '</div>';
                            }

                          
                            var googleNewDisplay = "";
                            if (simResult && simResult.totalGoogleOffers) {
                                googleNewDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                                    + ' Pozycja cenowa | '
                                    + '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:16px; height:16px;" /> '
                                    + simResult.newGoogleRanking + ' / ' + simResult.totalGoogleOffers
                                    + '</div>';
                            }
                            var ceneoNewDisplay = "";
                            if (simResult && simResult.totalCeneoOffers) {
                                ceneoNewDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                                    + ' Pozycja cenowa | '
                                    + '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:16px; height:16px;" /> '
                                    + simResult.newCeneoRanking + ' / ' + simResult.totalCeneoOffers
                                    + '</div>';
                            }

                           
                            let currentBlock = '<div class="price-info-box">';
                            currentBlock += '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                                + 'Cena produktu | ' + item.currentPrice.toFixed(2) + ' PLN</div>';
                            if (googleDisplay) currentBlock += googleDisplay;
                            if (ceneoDisplay) currentBlock += ceneoDisplay;

                            if (simResult && simResult.currentMargin != null && simResult.currentMarginValue != null) {
                                let currMarginValue = parseFloat(simResult.currentMarginValue).toFixed(2);
                                let currMarginPercentage = parseFloat(simResult.currentMargin).toFixed(2);
                                let currSign = parseFloat(simResult.currentMarginValue) >= 0 ? "+" : "";
                                let currClass = parseFloat(simResult.currentMarginValue) >= 0
                                    ? "priceBox-diff-margin"
                                    : "priceBox-diff-margin-minus";

                                currentBlock += `
                                <div class="price-info-item">
                                    <div class="price-box-diff-margin ${currClass}">
                                        <p>Marża: ${currSign}${currMarginValue} PLN (${currSign}${currMarginPercentage}%)</p>
                                    </div>
                                </div>`;
                            }
                            currentBlock += '</div>';

                      
                            let newBlock = '<div class="price-info-box">';
                            newBlock += '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                                + 'Cena produktu | ' + item.newPrice.toFixed(2) + ' PLN</div>';
                            if (googleNewDisplay) newBlock += googleNewDisplay;
                            if (ceneoNewDisplay) newBlock += ceneoNewDisplay;

                         
                            if (simResult && simResult.newMargin != null && simResult.newMarginValue != null) {
                                let newMarginValue = parseFloat(simResult.newMarginValue).toFixed(2);
                                let newMarginPercentage = parseFloat(simResult.newMargin).toFixed(2);
                                let newSign = parseFloat(simResult.newMarginValue) >= 0 ? "+" : "";
                                let newClass = parseFloat(simResult.newMarginValue) >= 0
                                    ? "priceBox-diff-margin"
                                    : "priceBox-diff-margin-minus";

                                newBlock += `
                                <div class="price-info-item">
                                    <div class="price-box-diff-margin ${newClass}">
                                        <p>Marża: ${newSign}${newMarginValue} PLN (${newSign}${newMarginPercentage}%)</p>
                                    </div>
                                </div>`;
                            }
                            newBlock += '</div>';

                            modalContent += '<tr>';

                            if (hasImage) {
                                if (imageUrl && imageUrl.trim() !== "") {
                                    modalContent += `
                                    <td>
                                        <div class="price-info-item" style="padding:4px;">
                                            <img
                                                data-src="${imageUrl}"
                                                alt="${name}"
                                                class="lazy-load loaded"
                                                style="width: 114px; height: 114px; margin-right: 5px; margin-left: 5px; background-color: #fff; border: 1px solid #e3e3e3; border-radius: 4px; padding: 10px; display: block;"
                                                src="${imageUrl}"
                                            >
                                        </div>
                                    </td>`;
                                } else {
                                    modalContent += '<td></td>';
                                }
                            }

                           
                            modalContent += '<td>' +
                                '<div class="price-info-item" style="font-size:125%;">' + name + '</div>' +
                                (ean ? '<div class="price-info-item">EAN: ' + ean + '</div>' : '') +
                                '</td>';

                       
                            modalContent += '<td>' + currentBlock + '</td>';

                          
                            modalContent += '<td style="font-size:16px; white-space: nowrap;">' +
                                arrow + ' ' + Math.abs(diff).toFixed(2) + ' PLN</td>';

                          
                            modalContent += '<td>' + newBlock + '</td>';

                        
                            modalContent += '<td>' +
                                '<button class="remove-change-btn" data-product-id="' + item.productId + '" style="background: none; border: none; padding: 5px; display: flex; align-items: center; margin-left:8px; justify-content: center;">' +
                                '<i class="fa fa-trash-o" style="font-size: 19px; color: red;"></i>' +
                                '</button>' +
                                '</td>';

                            modalContent += '</tr>';
                        });

                        modalContent += '</tbody></table>';

                        var tableContainer = document.getElementById("simulationModalBody");
                        if (tableContainer) {
                            tableContainer.innerHTML = modalContent;
                        }

                     
                        document.querySelectorAll('.remove-change-btn').forEach(function (btn) {
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

                       
                        var simulationModal = document.getElementById("simulationModal");
                        simulationModal.style.display = 'block';
                        simulationModal.classList.add('show');
                    })
                    .catch(function (err) {
                     
                    });
            })
            .catch(function (err) {
                console.error("Błąd pobierania danych produktu:", err);
            });
    }


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
    

        let csvContent = "EAN,Nowa cena\n";
        selectedPriceChanges.forEach(item => {
            const pid = parseInt(item.productId);
            const ean = eanMapGlobal[pid] || "";
        
            const eanText = ean ? `="${ean}"` : "";
            const newPrice = item.newPrice != null ? item.newPrice.toFixed(2).replace('.', ',') : "";
            csvContent += `${eanText},${newPrice}\n`;
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
            { header: "EAN", key: "ean", width: 18, style: { numFmt: "@" } },
            { header: "Nowa Cena", key: "newPrice", width: 15, style: { numFmt: '#,##0.00' } }
        ];

        selectedPriceChanges.forEach(item => {
            const pid = parseInt(item.productId);
            const ean = eanMapGlobal[pid] || "";
            const eanStr = String(ean);
            const newPrice = item.newPrice != null ? parseFloat(item.newPrice.toFixed(2)) : null;
            worksheet.addRow({ ean: eanStr, newPrice: newPrice });
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

  
    var closeButtons = document.querySelectorAll('#simulationModal .close, #simulationModal [data-dismiss="modal"]');
    closeButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            var simulationModal = document.getElementById("simulationModal");
            simulationModal.style.display = 'none';
            simulationModal.classList.remove('show');
   
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







