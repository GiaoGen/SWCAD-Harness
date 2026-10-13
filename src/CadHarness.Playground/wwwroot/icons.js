import createElement from './vendor/lucide/createElement.js';
import Plus from './vendor/lucide/plus.js';
import X from './vendor/lucide/x.js';
import Moon from './vendor/lucide/moon.js';

const icons = { plus: Plus, x: X, moon: Moon };
window.playgroundIcon = name => createElement(icons[name], { width: 16, height: 16, 'aria-hidden': 'true' });
for (const id of ['addRequirement', 'addRule', 'addQuery']) {
  const button = document.getElementById(id); button.textContent = button.textContent.replace(/^\+ /, ''); button.prepend(window.playgroundIcon('plus'));
}
const theme = document.getElementById('theme'); theme.replaceChildren(window.playgroundIcon('moon')); theme.title = 'Toggle light / dark'; theme.setAttribute('aria-label', theme.title);
