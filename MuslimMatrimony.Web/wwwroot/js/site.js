// No inline scripts are allowed by the Content-Security-Policy, so behaviour is wired up here.
document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('select.js-autosubmit').forEach(el =>
        el.addEventListener('change', () => el.form.submit()));

    document.querySelectorAll('time.js-localtime').forEach(el => {
        const d = new Date(el.getAttribute('datetime'));
        if (!isNaN(d)) {
            el.textContent = d.toLocaleString(document.documentElement.lang, { dateStyle: 'medium', timeStyle: 'short' });
        }
    });

    const chat = document.getElementById('chat');
    if (chat) {
        chat.scrollTop = chat.scrollHeight;
    }
});
