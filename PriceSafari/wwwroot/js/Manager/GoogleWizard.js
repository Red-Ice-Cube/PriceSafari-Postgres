const googleDataEl = document.getElementById("google-wizard-data");

const storeId = parseInt(googleDataEl.getAttribute("data-store-id") || "0", 10);
const xmlContainer = document.getElementById("xmlContainer");

let existingMappingsJson = googleDataEl.getAttribute("data-existing-mappings") || "[]";

let existingMappings = [];
try {
    existingMappings = JSON.parse(existingMappingsJson);
} catch (e) {
    console.error("Błąd parsowania existingMappingsJson:", e);
    existingMappings = [];
}

let mappingForField = {
    "ExternalId": null,
    "Url": null,
    "GoogleEan": null,
    "GoogleImage": null,
    "GoogleExportedName": null,
    "GoogleExportedProducer": null,
    "GoogleExportedProducerCode": null,
    "GoogleXMLPrice": null,
    "GoogleDeliveryXMLPrice": null
};

existingMappings.forEach(m => {
    if (m.fieldName) {
        mappingForField[m.fieldName] = {
            xpath: m.localName,
            nodeCount: 0,
            firstValue: ""
        };
    }
});

let xmlDoc = null;
let proxyUrl = `/GoogleImportWizardXml/ProxyXml?storeId=${storeId}`;

function processXmlString(xmlStr, sourceDescription) {
    console.log(`--- Rozpoczynanie przetwarzania XML ${sourceDescription} ---`);

    if (xmlContainer) {
        xmlContainer.innerHTML = `<i>Parsowanie XML (${sourceDescription})...</i>`;
    } else {
        console.error("Nie znaleziono elementu xmlContainer!");
        return;
    }

    if (!xmlStr || xmlStr.trim().length === 0) {
        console.error(`Błąd: Otrzymano pusty ciąg XML z ${sourceDescription}.`);
        xmlContainer.innerText = `Błąd: Otrzymano pusty ciąg XML z ${sourceDescription}.`;
        return;
    }

    let parser = new DOMParser();
    xmlDoc = parser.parseFromString(xmlStr, "application/xml");

    if (!xmlDoc || xmlDoc.documentElement.nodeName === "parsererror") {
        let errorContent = xmlDoc && xmlDoc.documentElement ? xmlDoc.documentElement.textContent : "Nieznany błąd parsera";
        console.error(`Błąd parsowania XML z ${sourceDescription}:`, errorContent);
        xmlContainer.innerText =
            `Błąd parsowania XML (${sourceDescription}):\n` + errorContent +
            "\n\nSprawdź konsolę (F12) po więcej szczegółów. Spróbuj wczytać plik lokalny, jeśli problem dotyczy ładowania sieciowego.";
        return;
    }

    console.log(`XML z ${sourceDescription} sparsowany pomyślnie.`);
    xmlContainer.innerHTML = '';
    try {

        if (xmlDoc.documentElement) {
            buildXmlTree(xmlDoc.documentElement, "");
            applyExistingMappings();
            renderMappingTable();
        } else {
            throw new Error("Dokument XML nie zawiera elementu głównego.");
        }
    } catch (e) {
        console.error(`Błąd podczas budowania drzewa lub stosowania mapowań z ${sourceDescription}:`, e);
        xmlContainer.innerText = `Wystąpił błąd podczas wizualizacji XML lub stosowania mapowań z ${sourceDescription}: ${e.message}. Sprawdź konsolę (F12).`;
    }
}

function buildXmlTree(node, parentPath) {
    let container = document.getElementById("xmlContainer");
    if (!parentPath && !container.querySelector("ul")) {
        let ulRoot = document.createElement("ul");
        ulRoot.appendChild(createLi(node, parentPath));
        container.appendChild(ulRoot);
    } else {
        let rootUl = container.querySelector("ul");
        rootUl.appendChild(createLi(node, parentPath));
    }
}

function createLi(node, parentPath) {
    let li = document.createElement("li");
    li.classList.add("xml-node");

    let nodeName = node.nodeName;
    let nameAttr = node.getAttribute && node.getAttribute("name");
    if (nameAttr) {
        nodeName += `[@name="${nameAttr}"]`;
    }

    let currentPath = parentPath ?
        parentPath + "/" + nodeName :
        "/" + nodeName;

    li.setAttribute("data-xpath", currentPath);

    let b = document.createElement("b");
    b.innerText = node.nodeName + (nameAttr ? ` (name="${nameAttr}")` : "");
    li.appendChild(b);

    if (node.attributes && node.attributes.length > 0) {
        let ulAttrs = document.createElement("ul");
        Array.from(node.attributes).forEach(attr => {

            if (attr.name === "name") return;

            let attrLi = document.createElement("li");
            attrLi.classList.add("xml-node");
            let attrPath = currentPath + `/@${attr.name}`;
            attrLi.setAttribute("data-xpath", attrPath);

            let attrSpan = document.createElement("span");
            attrSpan.innerText = `${attr.value}`;
            attrLi.appendChild(attrSpan);

            ulAttrs.appendChild(attrLi);
        });
        if (ulAttrs.children.length > 0) {
            li.appendChild(ulAttrs);
        }
    }

    let textVal = node.textContent.trim();
    if (node.children.length === 0 && textVal) {
        let ulVal = document.createElement("ul");
        let liVal = document.createElement("li");
        liVal.classList.add("xml-node");
        let valPath = currentPath + "/#value";
        liVal.setAttribute("data-xpath", valPath);

        let spanVal = document.createElement("span");
        spanVal.innerText = textVal;
        liVal.appendChild(spanVal);

        ulVal.appendChild(liVal);
        li.appendChild(ulVal);
    }

    if (node.children.length > 0) {
        let ul = document.createElement("ul");
        Array.from(node.children).forEach(child => {
            ul.appendChild(createLi(child, currentPath));
        });
        li.appendChild(ul);
    }

    return li;
}

document.addEventListener("click", function (e) {
    let el = e.target.closest(".xml-node");
    if (!el) return;
    let xPath = el.getAttribute("data-xpath");
    let selectedField = document.getElementById("fieldSelector").value;

    document.querySelectorAll(`.highlight-${selectedField}`)
        .forEach(x => x.classList.remove(`highlight-${selectedField}`));

    let sameNodes = document.querySelectorAll(`.xml-node[data-xpath='${xPath}']`);
    sameNodes.forEach(n => n.classList.add(`highlight-${selectedField}`));

    let count = sameNodes.length;
    let firstVal = "";
    if (count > 0) {
        let sp = sameNodes[0].querySelector("span");
        if (sp) {
            firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
        }
    }

    mappingForField[selectedField] = {
        xpath: xPath,
        nodeCount: count,
        firstValue: firstVal
    };
    renderMappingTable();
});

function applyExistingMappings() {
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        if (!info || !info.xpath) continue;

        let sameNodes = document.querySelectorAll(`.xml-node[data-xpath='${info.xpath}']`);
        let count = sameNodes.length;
        let firstVal = "";
        if (count > 0) {
            let sp = sameNodes[0].querySelector("span");
            if (sp) {
                firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
            }
        }
        info.nodeCount = count;
        info.firstValue = firstVal;
        sameNodes.forEach(n => n.classList.add(`highlight-${fieldName}`));
    }
    renderMappingTable();
}

function clearAllHighlights() {
    Object.keys(mappingForField).forEach(field => {
        document.querySelectorAll(`.highlight-${field}`)
            .forEach(el => el.classList.remove(`highlight-${field}`));
    });
}

function loadFromNetwork() {
    console.log(`Próba załadowania XML z sieci: ${proxyUrl}`);
    if (xmlContainer) {
        xmlContainer.innerHTML = `<i>Ładowanie XML z sieci: ${proxyUrl}...</i>`;
    }

    fetch(proxyUrl)
        .then(resp => {

            if (!resp.ok) {

                throw new Error(`Błąd sieci: ${resp.status} ${resp.statusText}`);
            }
            console.log("Status sieci:", resp.status);
            console.log("Typ zawartości z sieci:", resp.headers.get('Content-Type'));
            return resp.text();
        })
        .then(xmlStr => {
            console.log("--- Surowy tekst z sieci (pierwsze 1000 znaków) ---");
            console.log(xmlStr ? xmlStr.substring(0, 1000) : "[Pusta odpowiedź]");
            console.log("--- Koniec podglądu surowego tekstu ---");

            let startIndex = xmlStr ? xmlStr.indexOf('<') : -1;
            let cleanedXmlStr = xmlStr;

            if (startIndex === -1) {

                throw new Error("Odpowiedź z sieci nie zawiera znaku '<'. Nieprawidłowy format.");
            }
            if (startIndex > 0) {

                console.warn(`Czyszczenie odpowiedzi sieciowej: Usunięto ${startIndex} znaków z początku.`);
                cleanedXmlStr = xmlStr.substring(startIndex);
            } else {

                if (cleanedXmlStr && cleanedXmlStr.charCodeAt(0) === 65279) {
                    console.warn("Czyszczenie odpowiedzi sieciowej: Usunięto znak BOM z początku.");
                    cleanedXmlStr = cleanedXmlStr.substring(1);
                }
            }

            processXmlString(cleanedXmlStr, `Sieć (${proxyUrl})`);
        })
        .catch(err => {

            console.error("Błąd podczas ładowania lub wstępnego przetwarzania XML z sieci:", err);
            if (xmlContainer) {
                xmlContainer.innerText = `Wystąpił błąd podczas ładowania danych z sieci:\n${err.message}\n\nMożesz spróbować wczytać dane z zapisanego pliku lokalnego.`;
            }
        });
}

const processButton = document.getElementById('processXmlButton');
if (processButton) {
    processButton.addEventListener('click', () => {
        const fileInput = document.getElementById('xmlFileInput');

        if (!fileInput) {
            alert('Błąd: Nie znaleziono elementu do wyboru pliku (xmlFileInput).');
            return;
        }

        if (fileInput.files.length === 0) {
            alert('Najpierw wybierz plik XML/TXT.');
            return;
        }
        const file = fileInput.files[0];
        const reader = new FileReader();

        reader.onload = function (event) {

            processXmlString(event.target.result, `Plik (${file.name})`);
        };

        reader.onerror = function (event) {
            console.error("Błąd odczytu pliku lokalnego:", event.target.error);
            if (xmlContainer) {
                xmlContainer.innerText = 'Błąd odczytu pliku lokalnego: ' + event.target.error;
            }
        };

        if (xmlContainer) {
            xmlContainer.innerHTML = `<i>Wczytywanie pliku lokalnego: ${file.name}...</i>`;
        }
        reader.readAsText(file);
    });
} else {
    console.warn("Nie znaleziono przycisku 'processXmlButton'. Ładowanie z pliku nie będzie działać.");
}

function renderMappingTable() {
    let tbody = document.getElementById("mappingTable").querySelector("tbody");
    tbody.innerHTML = "";
    for (let fieldName in mappingForField) {
        if (!fieldName || fieldName === "undefined") continue;
        let info = mappingForField[fieldName];
        let tr = document.createElement("tr");
        if (!info) {
            tr.innerHTML = `
                <td>${fieldName}</td>
                <td>-</td>
                <td>0</td>
                <td>-</td>
                `;
        } else {
            tr.innerHTML = `
                <td>${fieldName}</td>
                <td>${info.xpath || "-"}</td>
                <td>${info.nodeCount || 0}</td>
                <td>${info.firstValue || "-"}</td>
                `;
        }
        tbody.appendChild(tr);
    }
}

document.getElementById("saveMapping").addEventListener("click", function () {
    let finalMappings = [];
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        if (info && info.xpath) {
            finalMappings.push({
                fieldName: fieldName,
                localName: info.xpath
            });
        }
    }
    fetch(`/GoogleImportWizardXml/SaveGoogleMappings?storeId=${storeId}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(finalMappings)
    })
        .then(r => r.json())
        .then(d => alert(d.message))
        .catch(err => console.error(err));
});

document.getElementById("reloadMappings").addEventListener("click", function () {
    fetch(`/GoogleImportWizardXml/GetGoogleMappings?storeId=${storeId}`)
        .then(r => r.json())
        .then(data => {
            console.log("Odebrano mapowania z bazy (reload):", data);
            clearAllHighlights();

            Object.keys(mappingForField).forEach(f => mappingForField[f] = null);
            data.forEach(m => {
                if (m.fieldName) {
                    mappingForField[m.fieldName] = {
                        xpath: m.localName,
                        nodeCount: 0,
                        firstValue: ""
                    };
                }
            });
            applyExistingMappings();
        })
        .catch(err => console.error("Błąd getGoogleMappings:", err));
});

function parsePrice(value) {
    if (!value) return null;
    let match = value.match(/([\d]+([.,]\d+)?)/);
    if (!match) return null;
    let numericString = match[1].replace(',', '.');
    let floatVal = parseFloat(numericString);
    if (isNaN(floatVal)) return null;
    return floatVal.toFixed(2);
}

function extractProductsFromXml() {
    if (!xmlDoc) {
        alert("Brak XML do parsowania");
        return;
    }

    const mappedXPaths = Object.values(mappingForField)
        .filter(m => m && m.xpath)

        .map(m => m.xpath.replace('/#value', '').replace(/\/\@.*/, ''));

    if (mappedXPaths.length === 0) {
        alert("Proszę najpierw zmapować przynajmniej jedno pole (np. ID Produktu), aby można było zidentyfikować strukturę produktu.");
        document.getElementById("productMapsPreview").textContent = "[]";
        return;
    }

    const pathParts = mappedXPaths.map(p => p.split('/'));
    let commonPath = [];
    if (pathParts.length > 0) {
        for (let i = 0; i < pathParts[0].length; i++) {
            const segment = pathParts[0][i];
            if (pathParts.every(p => p.length > i && p[i] === segment)) {
                commonPath.push(segment);
            } else {
                break;
            }
        }
    }

    if (commonPath.length <= 1) {
        alert("Nie udało się automatycznie zidentyfikować głównego węzła produktu. Sprawdź, czy mapowania są spójne i wskazują na elementy wewnątrz tego samego typu obiektu.");
        return;
    }

    const productNodeName = commonPath[commonPath.length - 1];
    console.log("Automatycznie wykryto węzeł produktu:", productNodeName);

    let entries = xmlDoc.getElementsByTagName(productNodeName);

    if (entries.length === 0) {
        alert(`Nie znaleziono żadnych elementów o nazwie <${productNodeName}>. Sprawdź, czy mapowania są spójne.`);
        document.getElementById("productMapsPreview").textContent = "[]";
        return;
    }

    let onlyEanProducts = document.getElementById("onlyEanProducts").checked;
    let productMaps = [];
    let countUrlsWithParams = 0;
    let removeParams = document.getElementById("cleanUrlParameters").checked;

    for (let i = 0; i < entries.length; i++) {
        let entryNode = entries[i];
        let pm = {
            StoreId: storeId.toString(),

            ExternalId: getVal(entryNode, "ExternalId", productNodeName),
            Url: getVal(entryNode, "Url", productNodeName),
            GoogleEan: getVal(entryNode, "GoogleEan", productNodeName),
            GoogleImage: getVal(entryNode, "GoogleImage", productNodeName),
            GoogleExportedName: getVal(entryNode, "GoogleExportedName", productNodeName),
            GoogleExportedProducer: getVal(entryNode, "GoogleExportedProducer", productNodeName),
            GoogleExportedProducerCode: getVal(entryNode, "GoogleExportedProducerCode", productNodeName),
            GoogleXMLPrice: parsePrice(getVal(entryNode, "GoogleXMLPrice", productNodeName)),
            GoogleDeliveryXMLPrice: parsePrice(getVal(entryNode, "GoogleDeliveryXMLPrice", productNodeName))
        };

        if (onlyEanProducts && (!pm.GoogleEan || !pm.GoogleEan.trim())) {
            continue;
        }

        if (pm.Url) {
            let qIdx = pm.Url.indexOf('?');
            if (qIdx !== -1) {
                countUrlsWithParams++;
                if (removeParams) {
                    pm.Url = pm.Url.substring(0, qIdx);
                }
            }
        }
        productMaps.push(pm);
    }

    console.log("Wyciągnięto productMaps(front):", productMaps);
    console.log("Liczba URLi zawierających parametry:", countUrlsWithParams);

    let previewText = JSON.stringify(productMaps, null, 2);
    document.getElementById("productMapsPreview").textContent = previewText;

    document.getElementById("urlParamsInfo").textContent =
        "Liczba URL zawierających parametry: " + countUrlsWithParams;

    if (document.getElementById("removeDuplicateUrls").checked) {
        let seen = {};
        let deduped = [];
        productMaps.forEach(pm => {
            if (pm.Url) {
                if (!seen[pm.Url]) {
                    seen[pm.Url] = true;
                    deduped.push(pm);
                }
            } else {
                deduped.push(pm);
            }
        });
        productMaps = deduped;
        document.getElementById("productMapsPreview").textContent =
            JSON.stringify(productMaps, null, 2);
    }

    if (document.getElementById("removeDuplicateEans").checked) {
        let seenEan = {};
        let dedupedEan = [];
        productMaps.forEach(pm => {
            if (pm.GoogleEan) {
                if (!seenEan[pm.GoogleEan]) {
                    seenEan[pm.GoogleEan] = true;
                    dedupedEan.push(pm);
                }
            } else {
                dedupedEan.push(pm);
            }
        });
        productMaps = dedupedEan;
        document.getElementById("productMapsPreview").textContent =
            JSON.stringify(productMaps, null, 2);
    }

    if (document.getElementById("removeDuplicateProducerCodes").checked) {
        let seenProducerCode = {};
        let dedupedProducerCode = [];
        productMaps.forEach(pm => {
            if (pm.GoogleExportedProducerCode) {
                if (!seenProducerCode[pm.GoogleExportedProducerCode]) {
                    seenProducerCode[pm.GoogleExportedProducerCode] = true;
                    dedupedProducerCode.push(pm);
                }
            } else {
                dedupedProducerCode.push(pm);
            }
        });
        productMaps = dedupedProducerCode;
        document.getElementById("productMapsPreview").textContent =
            JSON.stringify(productMaps, null, 2);
    }

    document.getElementById("finalNodesInfo").textContent =
        "Final nodes po filtrach: " + productMaps.length;

    checkDuplicates(productMaps);
}

function checkDuplicates(productMaps) {
    let urlCounts = {};
    let eanCounts = {};
    let producerCodeCounts = {};

    productMaps.forEach(pm => {
        if (pm.Url) {
            urlCounts[pm.Url] = (urlCounts[pm.Url] || 0) + 1;
        }
        if (pm.GoogleEan) {
            eanCounts[pm.GoogleEan] = (eanCounts[pm.GoogleEan] || 0) + 1;
        }
        if (pm.GoogleExportedProducerCode) {
            producerCodeCounts[pm.GoogleExportedProducerCode] = (producerCodeCounts[pm.GoogleExportedProducerCode] || 0) + 1;
        }
    });

    let totalUniqueUrls = Object.keys(urlCounts).length;
    let duplicateUrls = 0;
    for (let key in urlCounts) {
        if (urlCounts[key] > 1) {
            duplicateUrls++;
        }
    }

    let totalUniqueEans = Object.keys(eanCounts).length;
    let duplicateEans = 0;
    for (let key in eanCounts) {
        if (eanCounts[key] > 1) {
            duplicateEans++;
        }
    }

    let totalUniqueProducerCodes = Object.keys(producerCodeCounts).length;
    let duplicateProducerCodes = 0;
    for (let key in producerCodeCounts) {
        if (producerCodeCounts[key] > 1) {
            duplicateProducerCodes++;
        }
    }

    let urlMessage = `Unikalnych URL: ${totalUniqueUrls} `;
    urlMessage += duplicateUrls > 0 ?
        `<span style="color:red;">(Duplikatów: ${duplicateUrls})</span>` :
        `(Brak duplikatów)`;

    let eanMessage = `Unikalnych kodów EAN: ${totalUniqueEans} `;
    eanMessage += duplicateEans > 0 ?
        `<span style="color:red;">(Duplikatów: ${duplicateEans})</span>` :
        `(Brak duplikatów)`;

    let producerCodeMessage = `Unikalnych kodów producenta: ${totalUniqueProducerCodes} `;
    producerCodeMessage += duplicateProducerCodes > 0 ?
        `<span style="color:red;">(Duplikatów: ${duplicateProducerCodes})</span>` :
        `(Brak duplikatów)`;

    document.getElementById("duplicatesInfo").innerHTML =
        urlMessage + "<br>" + eanMessage + "<br>" + producerCodeMessage;
}

document.getElementById("extractProducts").addEventListener("click", function () {
    extractProductsFromXml();
});

document.getElementById("cleanUrlParameters").addEventListener("change", function () {
    extractProductsFromXml();
});

document.getElementById("removeDuplicateUrls").addEventListener("change", function () {
    extractProductsFromXml();
});

document.getElementById("removeDuplicateEans").addEventListener("change", function () {
    extractProductsFromXml();
});

document.getElementById("removeDuplicateProducerCodes").addEventListener("change", function () {
    extractProductsFromXml();
});
function getVal(entryNode, fieldName, productNodeName) {
    const info = mappingForField[fieldName];

    if (!info || !info.xpath || !productNodeName) {
        return null;
    }

    let path = info.xpath;
    const contextNode = entryNode;

    const productNodePathPart = `/${productNodeName}/`;
    const lastIndexOfProductNode = path.lastIndexOf(productNodePathPart);

    let relativePath;
    if (lastIndexOfProductNode !== -1) {

        relativePath = '.' + path.substring(lastIndexOfProductNode + productNodePathPart.length - 1);
    } else {
        console.error(`Nie można ustalić ścieżki względnej dla "${path}" w oparciu o węzeł "${productNodeName}"`);
        return null;
    }

    if (relativePath.endsWith('/#value')) {

        relativePath = relativePath.slice(0, -6) + 'text()';
    }

    relativePath = relativePath.replace(/\/\@/g, '@');

    try {

        const result = xmlDoc.evaluate(relativePath, contextNode, null, XPathResult.STRING_TYPE, null);
        return result.stringValue.trim();
    } catch (e) {
        console.error(`Błąd wykonania XPath: "${relativePath}" w kontekście ${contextNode.tagName}`, e);
        return null;
    }
}
document.getElementById("saveProductMapsInDb").addEventListener("click", function () {
    let txt = document.getElementById("productMapsPreview").textContent.trim();
    if (!txt) {
        alert("Brak productMaps do zapisania!");
        return;
    }
    let productMaps = [];
    try {
        productMaps = JSON.parse(txt);
    } catch (e) {
        alert("Błędny JSON w productMapsPreview!");
        return;
    }
    fetch("/GoogleImportWizardXml/SaveProductMapsFromFront", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(productMaps)
    })
        .then(r => r.json())
        .then(d => alert("Zapisano: " + d.message))
        .catch(err => console.error(err));
});

renderMappingTable();

loadFromNetwork();