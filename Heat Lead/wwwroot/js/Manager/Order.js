$(document).ready(function () {
    $('#orderSearch').on('keyup', function () {
        var searchText = $(this).val().toLowerCase();

        $('.table-orders tbody tr').each(function () {
            var orderNumber = $(this).find('td:nth-child(2)').text().toLowerCase();
            $(this).toggle(orderNumber.indexOf(searchText) > -1);
        });
    });
});

$(document).ready(function () {
    $('.Button-Page-Small').on('click', function () {
        var orderId = $(this).data('order-id');
        $.ajax({
            url: changeOrderAcceptanceUrl,
            type: 'POST',
            data: { orderId: orderId },
            success: function (response) {
                location.reload();
            }
        });
    });
});


$(document).ready(function () {
    $('#productSearch').on('keyup', function () {
        var searchText = $(this).val().toLowerCase();

        $('.table-orders tbody tr').each(function () {
            var productName = $(this).find('td:nth-child(3)').text().toLowerCase();
            $(this).toggle(productName.indexOf(searchText) > -1);
        });
    });
});

$(document).ready(function () {
    $('#showAccepted, #showInValidation').on('change', function () {
        if (this.id == 'showAccepted' && this.checked) {
            $('#showInValidation').prop('checked', false);
        } else if (this.id == 'showInValidation' && this.checked) {
            $('#showAccepted').prop('checked', false);
        }
        filterRows();
    });

    function filterRows() {
        var showAccepted = $('#showAccepted').is(':checked');
        var showInValidation = $('#showInValidation').is(':checked');

        $('.table-orders tbody tr').each(function () {
            var isAccepted = $(this).find('.Validation').hasClass('Validation-accepted');
            var isInValidation = !isAccepted;

            if (showAccepted || showInValidation) {
                $(this).toggle((isAccepted && showAccepted) || (isInValidation && showInValidation));
            } else {
                $(this).show();
            }
        });
    }

    $('.sortable').on('click', function () {
        var table = $(this).parents('table').eq(0);
        var index = $(this).index();
        var rows = table.find('tr:gt(0)').toArray().sort(comparer(index));
        this.asc = !this.asc;

        if (!this.asc) { rows = rows.reverse(); }
        for (var i = 0; i < rows.length; i++) { table.append(rows[i]); }

        $('.sortable .sort-icon').not($(this).find('.sort-icon')).removeClass('asc').html('&#9660;');
        $(this).find('.sort-icon').toggleClass('asc', this.asc).html(this.asc ? '&#9650;' : '&#9660;');
    });

    function comparer(index) {
        return function (a, b) {
            var valA = getCellValue(a, index), valB = getCellValue(b, index);

            if (index == 4 || index == 5) {
                valA = parseFloat(valA.replace(' zł', '')) || valA;
                valB = parseFloat(valB.replace(' zł', '')) || valB;
            } else if (index == 6) {
                valA = new Date(valA.replace(' ', 'T')).getTime();
                valB = new Date(valB.replace(' ', 'T')).getTime();
            } else if (index == 7) {
                return valA === valB ? 0 : valA ? -1 : 1;
            }

            return $.isNumeric(valA) && $.isNumeric(valB) ? valA - valB : valA.toString().localeCompare(valB.toString());
        };
    }

    function getCellValue(row, index) {
        return $(row).children('td').eq(index).text();
    }
});