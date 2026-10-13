const MAX_QUANTITY = 99;
const API_URL = "/api/cart";

function updateQuantity(sku, quantity) {
    const safe = clampQuantity(quantity);
    return fetch(API_URL + "/" + sku, { method: "PUT", body: JSON.stringify({ quantity: safe }) });
}

const clampQuantity = (value) => Math.min(Math.max(1, value), MAX_QUANTITY);

function onQuantityChange(input) {
    updateQuantity(input.dataset.sku, Number(input.value));
}
