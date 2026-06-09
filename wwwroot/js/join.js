const $ = (id) => document.getElementById(id);
const params = new URLSearchParams(location.search);
const code = params.get('code');
const token = params.get('token');

$('joinBtn').onclick = async () => {
  const displayName = $('displayName').value.trim();
  const gender = $('gender').value;
  if (!displayName) return ($('result').textContent = 'Введи имя.');

  const loginRes = await fetch('/api/users/prototype-login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ displayName, gender })
  });
  if (!loginRes.ok) return ($('result').textContent = await loginRes.text());
  const user = await loginRes.json();
  localStorage.setItem('tod_user', JSON.stringify(user));

  const joinRes = await fetch('/api/games/join', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${user.token}` },
    body: JSON.stringify({ publicCode: code, token })
  });
  if (!joinRes.ok) return ($('result').textContent = await joinRes.text());
  const game = await joinRes.json();
  localStorage.setItem('tod_game_id', game.id);
  $('result').innerHTML = `<p>Ты в игре <b>${game.publicCode}</b>!</p><p><a href="/">Перейти к игре</a></p>`;
};
