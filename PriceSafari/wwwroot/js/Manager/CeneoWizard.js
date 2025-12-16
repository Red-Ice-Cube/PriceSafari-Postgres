const ceneoDataEl = document.getElementById("ceneo-wizard-data");
const storeId = parseInt(ceneoDataEl.getAttribute("data-store-id") || "0", 10);
const xmlContainer = document.getElementById("xmlContainer");

let existingMappingsJson = ceneoDataEl.getAttribute("data-existing-mappings") || "[]";
let existingMappings = [];
try {
    existingMappings = JSON.parse(existingMappingsJson);
} catch (e) {
    console.error("Błąd parsowania existingMappingsJson:", e);
    existingMappings = [];
}

let mappingForField = {
    ExternalId: null,
    Url: null,
    CeneoEan: null,
    CeneoImage: null,
    CeneoExportedName: null,
    CeneoExportedProducer: null,
    CeneoExportedProducerCode: null,
    CeneoXMLPrice: null,
    CeneoDeliveryXMLPrice: null
};

existingMappings.forEach(m => {
    if (m.fieldName && mappingForField.hasOwnProperty(m.fieldName)) {
        mappingForField[m.fieldName] = {
            xpath: m.localName,
            nodeCount: 0,
            firstValue: ""
        };
    }
});

let xmlDoc = null;
let proxyUrl = `/CeneoImportWizardXml/ProxyXml?storeId=${storeId}`;

function processXmlString(xmlStr, sourceDescription) {
    console.log(`--- Rozpoczynanie przetwarzania XML ${sourceDescription} ---`);

    if (xmlContainer) {
        xmlContainer.innerHTML = `<i>Parsowanie XML (${sourceDescription})...</i>`;
    }

    if (!xmlStr || xmlStr.trim().length === 0) {
        const msg = `Błąd: Otrzymano pusty ciąg XML z ${sourceDescription}.`;
        console.error(msg);
        if (xmlContainer) xmlContainer.innerText = msg;
        return;
    }

    let parser = new DOMParser();
    xmlDoc = parser.parseFromString(xmlStr, "application/xml");

    if (!xmlDoc || xmlDoc.documentElement.nodeName === "parsererror" || xmlDoc.getElementsByTagName("parsererror").length > 0) {
        let errorContent = "Błąd parsowania XML";
        const parserError = xmlDoc.getElementsByTagName("parsererror")[0];
        if (parserError) {
            errorContent = parserError.textContent;
        } else if (xmlDoc.documentElement) {
            errorContent = xmlDoc.documentElement.textContent;
        }

        console.error(`Błąd parsowania XML z ${sourceDescription}:`, errorContent);
        if (xmlContainer) {
            xmlContainer.innerText = `Błąd parsowania XML (${sourceDescription}):\n` + errorContent;
        }
        xmlDoc = null;
        return;
    }

    console.log(`XML z ${sourceDescription} sparsowany pomyślnie.`);
    if (xmlContainer) xmlContainer.innerHTML = '';

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
        if (xmlContainer) xmlContainer.innerText = `Wystąpił błąd: ${e.message}`;
    }
}

console.log(`Próba załadowania XML z sieci: ${proxyUrl}`);
if (xmlContainer) xmlContainer.innerHTML = `<i>Ładowanie XML z sieci...</i>`;

fetch(proxyUrl)
    .then(resp => {
        if (!resp.ok) throw new Error(`Błąd HTTP ${resp.status}: ${resp.statusText}`);
        return resp.text();
    })
    .then(xmlStr => {

        let cleanStr = xmlStr;
        if (cleanStr.charCodeAt(0) === 65279) cleanStr = cleanStr.substring(1);
        processXmlString(cleanStr, "Sieć");
    })
    .catch(err => {
        if (xmlContainer) {
            xmlContainer.innerText = "Błąd pobierania z sieci:\n" + err + "\nMożesz spróbować wczytać plik lokalnie.";
        }
        console.error("Błąd fetch XML:", err);
    });

const processButton = document.getElementById('processXmlButton');
if (processButton) {
    processButton.addEventListener('click', () => {
        const fileInput = document.getElementById('xmlFileInput');

        if (!fileInput) {
            alert('Błąd: Nie znaleziono elementu do wyboru pliku.');
            return;
        }

        if (fileInput.files.length === 0) {
            alert('Najpierw wybierz plik XML/TXT.');
            return;
        }

        const file = fileInput.files[0];
        const reader = new FileReader();

        reader.onload = function (event) {
            processXmlString(event.target.result, `Plik lokalny (${file.name})`);
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

    let currentPath = parentPath ? parentPath + "/" + nodeName : "/" + nodeName;
    li.setAttribute("data-xpath", currentPath);

    let b = document.createElement("b");
    b.innerText = node.nodeName + (nameAttr ? ` (name="${nameAttr}")` : "");
    li.appendChild(b);

    if (node.attributes && node.attributes.length > 0) {
        let ulAttrs = document.createElement("ul");
        Array.from(node.attributes).forEach(attr => {

            if (attr.name === "name" && nameAttr) return;

            let attrLi = document.createElement("li");
            attrLi.classList.add("xml-node", "xml-attribute");

            let attrPath = currentPath + `/@${attr.name}`;
            attrLi.setAttribute("data-xpath", attrPath);

            let attrLabel = document.createElement("span");

            attrLabel.innerHTML = `<i style="color: gray;">@${attr.name}:</i> ${attr.value}`;
            attrLi.appendChild(attrLabel);
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
        liVal.classList.add("xml-node", "xml-value");

        let valPath = currentPath + "/#value";
        liVal.setAttribute("data-xpath", valPath);

        let spanVal = document.createElement("span");
        spanVal.innerText = `: ${textVal}`;
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

document.addEventListener("click", e => {

    let el = e.target.closest(".xml-node");
    if (!el) return;

    let selectedField = document.getElementById("fieldSelector").value;

    let xPath = el.getAttribute("data-xpath");

    document.querySelectorAll(`.highlight-${selectedField}`)
        .forEach(x => x.classList.remove(`highlight-${selectedField}`));

    let sameNodes = queryNodesByXPath(xPath);

    sameNodes.forEach(n => n.classList.add(`highlight-${selectedField}`));

    let count = sameNodes.length;
    let firstVal = "";
    if (count > 0) {

        let sp = sameNodes[0].querySelector("span");
        if (sp) {

            firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();

            if (sameNodes[0].classList.contains('xml-attribute')) {
                firstVal = sp.textContent.replace(/^(\s*@\w+:\s*)/, "").trim();
            }
        }
    }

    mappingForField[selectedField] = { xpath: xPath, nodeCount: count, firstValue: firstVal };

    renderMappingTable();
});

function queryNodesByXPath(xPathValue) {

    let esc = escapeForSelector(xPathValue);
    return document.querySelectorAll(`.xml-node[data-xpath="${esc}"]`);
}

function escapeForSelector(val) {
    if (!val) return "";

    return val.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
}

function applyExistingMappings() {
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];

        if (!info || !info.xpath) continue;

        let sameNodes = queryNodesByXPath(info.xpath);
        let count = sameNodes.length;
        let firstVal = "";
        if (count > 0) {

            let sp = sameNodes[0].querySelector("span");
            if (sp) {
                firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
                if (sameNodes[0].classList.contains('xml-attribute')) {
                    firstVal = sp.textContent.replace(/^(\s*@\w+:\s*)/, "").trim();
                }
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

function renderMappingTable() {
    let tbody = document.getElementById("mappingTable").querySelector("tbody");
    tbody.innerHTML = "";

    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        let tr = document.createElement("tr");

        if (!info || !info.xpath) {
            tr.innerHTML = `<td>${fieldName}</td><td>-</td><td>0</td><td>-</td>`;
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

function parsePrice(value) {

    if (value === null || value === undefined || value === '') return null;

    let strValue = String(value).trim();

    let match = strValue.match(/^[^\d-]*?([+-]?\s?[\d.,\s]+(?:\.\d+)?(?:,\d+)?)[^\d.,]*?$/);

    if (!match) {
        match = strValue.match(/^[+-]?([\d.,\s]+)/);
    }

    if (!match || !match[1]) {
        console.warn(`parsePrice: Nie udało się wyekstrahować części numerycznej z '${value}'`);
        return null;
    }

    let numericString = match[1].trim();

    const lastDotIndex = numericString.lastIndexOf('.');
    const lastCommaIndex = numericString.lastIndexOf(',');

    let decimalSeparator = '.';
    let thousandSeparatorRegex = /[,]/g;

    if (lastCommaIndex > lastDotIndex) {

        decimalSeparator = ',';
        thousandSeparatorRegex = /[.]/g;
    } else if (lastDotIndex > lastCommaIndex) {

        decimalSeparator = '.';
        thousandSeparatorRegex = /[,]/g;
    }

    numericString = numericString.replace(/\s/g, '');

    numericString = numericString.replace(thousandSeparatorRegex, '');

    if (decimalSeparator === ',') {
        numericString = numericString.replace(',', '.');
    }

    let floatVal = parseFloat(numericString);
    if (isNaN(floatVal)) {
        console.warn(`parsePrice: Nie udało się sparsować '${numericString}' (po czyszczeniu z '${value}')`);
        return null;
    }

    return floatVal.toFixed(2);
}

document.getElementById("saveMapping").addEventListener("click", () => {
    let finalMappings = [];

    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        if (info && info.xpath) {

            finalMappings.push({ fieldName: fieldName, localName: info.xpath });
        }
    }

    fetch(`/CeneoImportWizardXml/SaveCeneoMappings?storeId=${storeId}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(finalMappings)
    })
        .then(r => r.json())
        .then(d => {
            if (d.success !== undefined) {
                alert(d.message);
            } else {

                alert(d);
            }
        })
        .catch(err => {
            console.error("Błąd zapisu mapowań:", err);
            alert("Wystąpił błąd podczas zapisywania mapowań.");
        });
});

document.getElementById("reloadMappings").addEventListener("click", () => {
    fetch(`/CeneoImportWizardXml/GetCeneoMappings?storeId=${storeId}`)
        .then(r => r.json())
        .then(data => {
            console.log("Odebrano mapowania z bazy (reload):", data);
            clearAllHighlights();

            Object.keys(mappingForField).forEach(f => mappingForField[f] = null);

            data.forEach(m => {
                if (m.fieldName && mappingForField.hasOwnProperty(m.fieldName)) {
                    mappingForField[m.fieldName] = { xpath: m.localName, nodeCount: 0, firstValue: "" };
                }
            });

            applyExistingMappings();
        })
        .catch(err => {
            console.error("Błąd getCeneoMappings:", err);
            alert("Wystąpił błąd podczas pobierania mapowań z bazy.");
        });
});

document.getElementById("extractProducts").addEventListener("click", () => {

    if (!xmlDoc) {
        alert("Brak XML do parsowania. Sprawdź, czy plik został poprawnie załadowany.");
        return;
    }

    let entries = [];
    const possibleProductTags = ["o", "item", "entry", "produkt", "product", "element"];
    let detectedTag = null;

    for (const tag of possibleProductTags) {
        const nodes = xmlDoc.getElementsByTagName(tag);
        if (nodes.length > 0) {
            entries = nodes;
            detectedTag = tag;
            console.log(`Wykryto tag produktu: <${detectedTag}> (${entries.length} elementów)`);
            break;
        }
    }

    if (entries.length === 0) {
        alert(`Nie znaleziono żadnego ze standardowych tagów produktu (${possibleProductTags.join(', ')}). Sprawdź strukturę XML.`);
        return;
    }

    const onlyEanProducts = document.getElementById("onlyEanProducts")?.checked ?? false;
    const removeParams = document.getElementById("cleanUrlParameters")?.checked ?? false;
    const removeDuplicateUrls = document.getElementById("removeDuplicateUrls")?.checked ?? false;
    const removeDuplicateEans = document.getElementById("removeDuplicateEans")?.checked ?? false;

    const removeDuplicateProducerCodes = document.getElementById("removeDuplicateProducerCodes")?.checked ?? false;

    let productMaps = [];
    let countUrlsWithParams = 0;

    for (let i = 0; i < entries.length; i++) {
        let entryNode = entries[i];

        let pm = {
            StoreId: storeId,
            ExternalId: getVal(entryNode, "ExternalId"),
            Url: getVal(entryNode, "Url"),
            CeneoEan: getVal(entryNode, "CeneoEan"),
            CeneoImage: getVal(entryNode, "CeneoImage"),
            CeneoExportedName: getVal(entryNode, "CeneoExportedName"),
            CeneoExportedProducer: getVal(entryNode, "CeneoExportedProducer"),
            CeneoExportedProducerCode: getVal(entryNode, "CeneoExportedProducerCode"),
            CeneoXMLPrice: parsePrice(getVal(entryNode, "CeneoXMLPrice")),
            CeneoDeliveryXMLPrice: parsePrice(getVal(entryNode, "CeneoDeliveryXMLPrice"))
        };

        if (onlyEanProducts && (!pm.CeneoEan || !pm.CeneoEan.trim())) {
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

    console.log("Surowe productMaps (przed deduplikacją):", productMaps);

    if (removeDuplicateUrls) {
        let seenUrls = new Set();
        productMaps = productMaps.filter(pm => {
            if (!pm.Url || !pm.Url.trim()) return true;
            if (seenUrls.has(pm.Url)) {
                return false;
            }
            seenUrls.add(pm.Url);
            return true;
        });
    }

    if (removeDuplicateEans) {
        let seenEans = new Set();
        productMaps = productMaps.filter(pm => {
            if (!pm.CeneoEan || !pm.CeneoEan.trim()) return true;
            if (seenEans.has(pm.CeneoEan)) {
                return false;
            }
            seenEans.add(pm.CeneoEan);
            return true;
        });
    }

    if (removeDuplicateProducerCodes) {
        let seenProducerCodes = new Set();
        productMaps = productMaps.filter(pm => {

            if (!pm.CeneoExportedProducerCode || !pm.CeneoExportedProducerCode.trim()) return true;

            if (seenProducerCodes.has(pm.CeneoExportedProducerCode)) {
                return false;
            }
            seenProducerCodes.add(pm.CeneoExportedProducerCode);
            return true;
        });
    }

    console.log("Finalne productMaps (po filtrach i deduplikacji):", productMaps);

    const previewElement = document.getElementById("productMapsPreview");
    if (previewElement) {
        previewElement.textContent = JSON.stringify(productMaps, null, 2);
    }

    const urlParamsInfo = document.getElementById("urlParamsInfo");
    if (urlParamsInfo) {
        urlParamsInfo.textContent = "Liczba URL zawierających parametry: " + countUrlsWithParams;
    }
    const finalNodesInfo = document.getElementById("finalNodesInfo");
    if (finalNodesInfo) {
        finalNodesInfo.textContent = "Finalna liczba produktów po filtrach: " + productMaps.length;
    }

    checkDuplicates(productMaps);
});

function getVal(entryNode, fieldName) {
    const info = mappingForField[fieldName];
    if (!info || !info.xpath) {

        return null;
    }

    let absolutePath = info.xpath;

    if (!entryNode || !entryNode.nodeName) {
        console.error("getVal: Nieprawidłowy entryNode");
        return null;
    }

    let entryNodePathSegments = [];
    let current = entryNode;

    while (current && current.nodeType === Node.ELEMENT_NODE && current !== xmlDoc.documentElement.parentNode) {
        let segment = current.nodeName;

        let nameAttr = current.getAttribute && current.getAttribute("name");
        if (nameAttr) {

            segment += `[@name="${nameAttr.replace(/"/g, '\\"')}"]`;
        }
        entryNodePathSegments.unshift(segment);
        current = current.parentNode;
    }

    let entryNodeAbsolutePath = entryNodePathSegments.length > 0 ? "/" + entryNodePathSegments.join("/") : "";

    let relativePath = "";

    let normalizedAbsolutePath = absolutePath.toLowerCase().replace(/\/#value$/, '');
    let normalizedEntryNodePath = entryNodeAbsolutePath.toLowerCase();

    if (normalizedAbsolutePath.startsWith(normalizedEntryNodePath + '/')) {

        relativePath = absolutePath.substring(entryNodeAbsolutePath.length + 1);

    }

    else if (normalizedAbsolutePath.startsWith(normalizedEntryNodePath + '/@')) {
        let lastSlashIndex = absolutePath.lastIndexOf('/');
        if (lastSlashIndex > -1 && absolutePath.substring(lastSlashIndex + 1).startsWith('@')) {
            relativePath = absolutePath.substring(lastSlashIndex + 1);

        }
    }

    else if (absolutePath.includes('/' + entryNode.nodeName + '/')) {
        let lastNodeNameIndex = absolutePath.lastIndexOf('/' + entryNode.nodeName + '/');
        if (lastNodeNameIndex !== -1) {
            relativePath = absolutePath.substring(lastNodeNameIndex + entryNode.nodeName.length + 2);

        }
    }

    else if (normalizedAbsolutePath === normalizedEntryNodePath && absolutePath.includes('@')) {
        let lastSlashIndex = absolutePath.lastIndexOf('/');
        if (lastSlashIndex > -1 && absolutePath.substring(lastSlashIndex + 1).startsWith('@')) {
            relativePath = absolutePath.substring(lastSlashIndex + 1);

        }
    }

    if (!relativePath) {
        console.warn(`Nie można ustalić ścieżki względnej. Pole: ${fieldName}, AbsPath: ${absolutePath}, EntryNodePath: ${entryNodeAbsolutePath}`);

        return null;
    }

    let path = relativePath;

    let currentNode = entryNode;

    const segments = path.split('/').filter(Boolean);

    for (let i = 0; i < segments.length; i++) {
        let seg = segments[i];

        if (!currentNode) {

            return null;
        }

        if (seg === '#value') {

            if (currentNode.nodeType === Node.ELEMENT_NODE || currentNode.nodeType === Node.TEXT_NODE) {

                return currentNode.textContent ? currentNode.textContent.trim() : null;
            } else {

                return null;
            }
        }

        if (seg.startsWith('@')) {
            const attrName = seg.substring(1);

            if (currentNode.nodeType !== Node.ELEMENT_NODE) {

                return null;
            }
            let attrValue = currentNode.getAttribute(attrName);

            if (i === segments.length - 1) {
                return attrValue;
            } else {
                console.error(`Błąd ścieżki: próba nawigacji dalej po atrybucie '${attrName}'`);
                return null;
            }
        }

        const bracketPos = seg.indexOf('[@name="');
        let child = null;
        let baseName = seg;
        let desiredNameAttr = null;

        if (bracketPos !== -1) {
            baseName = seg.substring(0, bracketPos);
            const inside = seg.substring(bracketPos + 8);
            const endBracket = inside.indexOf('"]');
            if (endBracket === -1) {
                console.error(`Niepoprawny format segmentu z predykatem: ${seg}`);
                return null;
            }
            desiredNameAttr = inside.substring(0, endBracket);
        }

        child = Array.from(currentNode.children).find(ch => {

            if (ch.nodeName === baseName) {
                if (desiredNameAttr !== null) {

                    return ch.getAttribute("name") === desiredNameAttr;
                } else {

                    return true;
                }
            }
            return false;
        });

        if (!child) {

            return null;
        }

        currentNode = child;
    }

    if (!segments[segments.length - 1]?.startsWith('@')) {

        return currentNode.textContent ? currentNode.textContent.trim() : null;
    } else {

        console.warn("Ścieżka zakończyła się atrybutem, ale wartość nie została zwrócona wcześniej?");
        return null;
    }
}

document.getElementById("saveProductMapsInDb").addEventListener("click", () => {
    const previewElement = document.getElementById("productMapsPreview");
    if (!previewElement) {
        alert("Brak elementu podglądu productMapsPreview!");
        return;
    }
    const txt = previewElement.textContent.trim();
    if (!txt) {
        alert("Brak danych produktów (productMaps) do zapisania! Kliknij najpierw 'Wyciągnij Produkty'.");
        return;
    }
    let productMaps;
    try {
        productMaps = JSON.parse(txt);
    } catch (e) {
        alert("Błędny format JSON w podglądzie productMapsPreview!");
        console.error("Błąd parsowania JSON:", e, "\nTekst:", txt);
        return;
    }

    if (!Array.isArray(productMaps)) {
        alert("Dane w podglądzie nie są poprawną tablicą produktów!");
        return;
    }

    console.log("Wysyłanie productMaps do zapisu:", productMaps);

    fetch("/CeneoImportWizardXml/SaveProductMapsFromFront", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(productMaps)
    })
        .then(r => r.json())
        .then(d => {

            if (d.success !== undefined) {
                alert((d.success ? "Sukces: " : "Błąd: ") + d.message);
            } else {
                alert("Otrzymano nieoczekiwaną odpowiedź z serwera.");
                console.log("Odpowiedź serwera:", d);
            }
        })
        .catch(err => {
            console.error("Błąd zapisu ProductMaps:", err);
            alert("Wystąpił błąd sieci lub serwera podczas zapisu produktów.");
        });
});

function checkDuplicates(productMaps) {
    if (!Array.isArray(productMaps)) {
        console.error("checkDuplicates: wejście nie jest tablicą");
        return;
    }
    const urlCounts = {};
    const eanCounts = {};

    const producerCodeCounts = {};
    let productsWithoutUrl = 0;
    let productsWithoutEan = 0;
    let productsWithoutProducerCode = 0;

    productMaps.forEach(pm => {
        if (pm.Url && pm.Url.trim()) {
            urlCounts[pm.Url] = (urlCounts[pm.Url] || 0) + 1;
        } else {
            productsWithoutUrl++;
        }
        if (pm.CeneoEan && pm.CeneoEan.trim()) {
            eanCounts[pm.CeneoEan] = (eanCounts[pm.CeneoEan] || 0) + 1;
        } else {
            productsWithoutEan++;
        }

        if (pm.CeneoExportedProducerCode && pm.CeneoExportedProducerCode.trim()) {
            producerCodeCounts[pm.CeneoExportedProducerCode] = (producerCodeCounts[pm.CeneoExportedProducerCode] || 0) + 1;
        } else {
            productsWithoutProducerCode++;
        }
    });

    const totalUniqueUrls = Object.keys(urlCounts).length;
    const duplicateUrlsCount = Object.values(urlCounts).filter(count => count > 1).length;
    const totalUniqueEans = Object.keys(eanCounts).length;
    const duplicateEansCount = Object.values(eanCounts).filter(count => count > 1).length;

    const totalUniqueProducerCodes = Object.keys(producerCodeCounts).length;
    const duplicateProducerCodesCount = Object.values(producerCodeCounts).filter(count => count > 1).length;

    let urlMessage = `Unikalnych URL: ${totalUniqueUrls}`;
    if (duplicateUrlsCount > 0) {
        urlMessage += ` <span style="color:red;">(Znaleziono ${duplicateUrlsCount} zduplikowanych wartości URL)</span>`;
    } else {
        urlMessage += ` (Brak duplikatów)`;
    }
    if (productsWithoutUrl > 0) {
        urlMessage += ` | Produktów bez URL: ${productsWithoutUrl}`;
    }

    let eanMessage = `Unikalnych EAN: ${totalUniqueEans}`;
    if (duplicateEansCount > 0) {
        eanMessage += ` <span style="color:red;">(Znaleziono ${duplicateEansCount} zduplikowanych wartości EAN)</span>`;
    } else {
        eanMessage += ` (Brak duplikatów)`;
    }
    if (productsWithoutEan > 0) {
        eanMessage += ` | Produktów bez EAN: ${productsWithoutEan}`;
    }

    let producerCodeMessage = `Unikalnych kodów prod.: ${totalUniqueProducerCodes}`;
    if (duplicateProducerCodesCount > 0) {
        producerCodeMessage += ` <span style="color:red;">(Znaleziono ${duplicateProducerCodesCount} zduplikowanych wartości)</span>`;
    } else {
        producerCodeMessage += ` (Brak duplikatów)`;
    }
    if (productsWithoutProducerCode > 0) {
        producerCodeMessage += ` | Produktów bez kodu: ${productsWithoutProducerCode}`;
    }

    const duplicatesInfoElement = document.getElementById("duplicatesInfo");
    if (duplicatesInfoElement) {

        duplicatesInfoElement.innerHTML = `${urlMessage}<br>${eanMessage}<br>${producerCodeMessage}`;
    }
}

const cleanUrlCheckbox = document.getElementById("cleanUrlParameters");
if (cleanUrlCheckbox) {
    cleanUrlCheckbox.addEventListener("change", () => {
        document.getElementById("extractProducts")?.click();
    });
}
const removeDupUrlsCheckbox = document.getElementById("removeDuplicateUrls");
if (removeDupUrlsCheckbox) {
    removeDupUrlsCheckbox.addEventListener("change", () => {
        document.getElementById("extractProducts")?.click();
    });
}
const removeDupEansCheckbox = document.getElementById("removeDuplicateEans");
if (removeDupEansCheckbox) {
    removeDupEansCheckbox.addEventListener("change", () => {
        document.getElementById("extractProducts")?.click();
    });
}
const onlyEanCheckbox = document.getElementById("onlyEanProducts");
if (onlyEanCheckbox) {
    onlyEanCheckbox.addEventListener("change", () => {
        document.getElementById("extractProducts")?.click();
    });
}

const removeDupProducerCodesCheckbox = document.getElementById("removeDuplicateProducerCodes");
if (removeDupProducerCodesCheckbox) {
    removeDupProducerCodesCheckbox.addEventListener("change", () => {
        document.getElementById("extractProducts")?.click();
    });
}

renderMappingTable();