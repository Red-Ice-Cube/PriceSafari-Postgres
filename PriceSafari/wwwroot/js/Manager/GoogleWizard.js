const googleDataEl = document.getElementById("google-wizard-data");

const storeId = parseInt(googleDataEl.getAttribute("data-store-id") || "0", 10);


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


fetch(proxyUrl)
    .then(resp => resp.text())
    .then(xmlStr => {
        let parser = new DOMParser();
        xmlDoc = parser.parseFromString(xmlStr, "application/xml");
        if (xmlDoc.documentElement.nodeName === "parsererror") {
            document.getElementById("xmlContainer").innerText =
                "Błąd parsowania XML:\n" + xmlDoc.documentElement.textContent;
            return;
        }
       
        buildXmlTree(xmlDoc.documentElement, "");
     
        applyExistingMappings();
    })
    .catch(err => {
        document.getElementById("xmlContainer").innerText =
            "Błąd pobierania XML:\n" + err;
    });


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

  
    let currentPath = parentPath
        ? parentPath + "/" + nodeName
        : "/" + nodeName;

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
    let entries = xmlDoc.getElementsByTagName("item").length > 0
        ? xmlDoc.getElementsByTagName("item")
        : xmlDoc.getElementsByTagName("entry");

 
    let onlyEanProducts = document.getElementById("onlyEanProducts").checked;

    let productMaps = [];
    let countUrlsWithParams = 0;
    let removeParams = document.getElementById("cleanUrlParameters").checked;

    for (let i = 0; i < entries.length; i++) {
        let entryNode = entries[i];
        let pm = {
            StoreId: storeId.toString(),
            ExternalId: getVal(entryNode, "ExternalId"),
            Url: getVal(entryNode, "Url"),
            GoogleEan: getVal(entryNode, "GoogleEan"),
            GoogleImage: getVal(entryNode, "GoogleImage"),
            GoogleExportedName: getVal(entryNode, "GoogleExportedName"),
            GoogleXMLPrice: parsePrice(getVal(entryNode, "GoogleXMLPrice")),
            GoogleDeliveryXMLPrice: parsePrice(getVal(entryNode, "GoogleDeliveryXMLPrice"))
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


    document.getElementById("finalNodesInfo").textContent =
        "Final nodes po filtrach: " + productMaps.length;

  
    checkDuplicates(productMaps);
}


function checkDuplicates(productMaps) {
    let urlCounts = {};
    let eanCounts = {};

    productMaps.forEach(pm => {
        if (pm.Url) {
            urlCounts[pm.Url] = (urlCounts[pm.Url] || 0) + 1;
        }
        if (pm.GoogleEan) {
            eanCounts[pm.GoogleEan] = (eanCounts[pm.GoogleEan] || 0) + 1;
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

    let urlMessage = `Unikalnych URL: ${totalUniqueUrls} `;
    urlMessage += duplicateUrls > 0
        ? `<span style="color:red;">(Duplikatów: ${duplicateUrls})</span>`
        : `(Brak duplikatów)`;

    let eanMessage = `Unikalnych kodów EAN: ${totalUniqueEans} `;
    eanMessage += duplicateEans > 0
        ? `<span style="color:red;">(Duplikatów: ${duplicateEans})</span>`
        : `(Brak duplikatów)`;

    document.getElementById("duplicatesInfo").innerHTML =
        urlMessage + "<br>" + eanMessage;
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


function getVal(entryNode, fieldName) {
    let info = mappingForField[fieldName];
    if (!info || !info.xpath) return null;

    let path = info.xpath;

 
    let possiblePrefixes = ["/rss/channel/item/", "/feed/entry/"];
    for (let i = 0; i < possiblePrefixes.length; i++) {
        if (path.startsWith(possiblePrefixes[i])) {
            path = path.substring(possiblePrefixes[i].length);
            break;
        }
    }
    if (path.startsWith("/")) {
        path = path.substring(1);
    }

  
    let segments = path.split('/');

    let currentNode = entryNode;
    for (let seg of segments) {
     
        if (seg.indexOf(':') !== -1) {
            seg = seg.split(':')[1];
        }
        if (!seg) return null;

        let child = Array.from(currentNode.children).find(e => e.localName === seg);
        if (!child) {
            return null;
        }
        currentNode = child;
    }
    return currentNode.textContent.trim();
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