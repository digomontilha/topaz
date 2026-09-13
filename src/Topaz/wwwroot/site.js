const form = document.querySelector('#shorten-form');
const button = document.querySelector('#submit');
const message = document.querySelector('#message');
const result = document.querySelector('#result');

form.addEventListener('submit', async (event) => {
    event.preventDefault();
    button.disabled = true;
    message.textContent = 'Gerando…';
    result.hidden = true;
    try {
        const response = await fetch(form.action, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ url: form.elements.url.value, alias: form.elements.alias.value })
        });
        const data = await response.json();
        if (!response.ok) {
            message.textContent = data.error ?? 'Não foi possível gerar o link. Confira os campos.';
            return;
        }
        const shortUrl = new URL(data.shortUrl, window.location.origin).href;
        result.href = shortUrl;
        result.textContent = shortUrl;
        result.hidden = false;
        message.textContent = 'Link criado! Selecione e copie o endereço abaixo.';
    } catch {
        message.textContent = 'Não foi possível comunicar com o servidor. Tente novamente.';
    } finally { button.disabled = false; }
});
