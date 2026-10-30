(() => {
  const key = 'clash.theme';
  const root = document.documentElement;
  let saved;
  try { saved = localStorage.getItem(key); } catch {}
  root.dataset.theme = saved === 'dark' ? 'dark' : 'light';

  document.addEventListener('DOMContentLoaded', () => {
    const button = document.getElementById('themeToggle');
    const label = document.getElementById('themeToggleLabel');
    const icon = document.getElementById('themeToggleIcon');
    function render() {
      const dark = root.dataset.theme === 'dark';
      label.textContent = dark ? 'Modo claro' : 'Modo escuro';
      icon.textContent = dark ? '☀' : '☾';
      button.setAttribute('aria-pressed', String(dark));
      button.setAttribute('aria-label', dark ? 'Ativar modo claro' : 'Ativar modo escuro');
      button.title = dark ? 'Ativar modo claro' : 'Ativar modo escuro';
    }
    button.addEventListener('click', () => {
      root.dataset.theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
      try { localStorage.setItem(key, root.dataset.theme); } catch {}
      render();
    });
    render();
  });
})();
