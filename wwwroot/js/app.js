const state = {
  user: JSON.parse(localStorage.getItem('tod_user') || 'null'),
  gameId: localStorage.getItem('tod_game_id') || '',
  inviteUrl: localStorage.getItem('tod_invite_url') || ''
};

const $ = (id) => document.getElementById(id);
let connection = null;

const categoryNames = {
  Light: 'Лайт 😊',
  Romantic: 'Романтика 💗',
  Deep: 'Глубоко 🌊',
  Flirty: 'Флирт 🔥'
};

function authHeaders() {
  const h = { 'Content-Type': 'application/json' };
  if (state.user?.token) h['Authorization'] = `Bearer ${state.user.token}`;
  return h;
}

function renderUser() {
  if (state.user) {
    $('userInfo').textContent = `Вы вошли как ${state.user.displayName} (${state.user.gender})`;
    $('loginCard').style.display = 'none';
    $('lobbyCard').style.display = '';
    if (state.gameId) loadGame();
  }
}

// --- Вход ---
$('loginBtn').onclick = async () => {
  const displayName = $('displayName').value.trim();
  const gender = $('gender').value;
  if (!displayName) return alert('Введи имя.');
  const res = await fetch('/api/users/prototype-login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ displayName, gender })
  });
  if (!res.ok) return alert(await res.text());
  state.user = await res.json();
  localStorage.setItem('tod_user', JSON.stringify(state.user));
  renderUser();
};

// --- Создать игру ---
$('createGameBtn').onclick = async () => {
  if (!state.user?.token) return alert('Сначала войди.');
  const category = $('categorySelect').value;
  const res = await fetch('/api/games', { method: 'POST', headers: authHeaders(), body: JSON.stringify({ maxPlayers: 2, category }) });
  if (!res.ok) return alert((await res.text()) || `Ошибка: ${res.status}`);
  const data = await res.json();
  state.gameId = data.id;
  state.inviteUrl = data.inviteUrl;
  localStorage.setItem('tod_game_id', data.id);
  localStorage.setItem('tod_invite_url', data.inviteUrl);
  $('createdGame').innerHTML = `<p>Отправь ссылку второму игроку:</p><p><a href="${data.inviteUrl}" target="_blank">${data.inviteUrl}</a></p>`;
  await loadGame();
};

// --- Пригласить (копировать ссылку) ---
$('inviteLink').onclick = async () => {
  if (!state.inviteUrl) return;
  await navigator.clipboard.writeText(state.inviteUrl);
  $('inviteLink').textContent = 'Скопировано!';
  setTimeout(() => { $('inviteLink').textContent = 'Пригласить'; }, 2000);
};

// --- Новая игра ---
$('newGameBtn').onclick = async () => {
  if (!confirm('Создать новую игру?')) return;
  localStorage.removeItem('tod_game_id');
  localStorage.removeItem('tod_invite_url');
  state.gameId = '';
  state.inviteUrl = '';
  connection = null;
  $('gameCard').classList.add('hidden');
  $('lobbyCard').style.display = '';
};

// --- Завершить игру ---
$('endGameBtn').onclick = async () => {
  if (!confirm('Точно завершить игру?')) return;
  const res = await fetch(`/api/games/${state.gameId}/end`, { method: 'POST', headers: authHeaders() });
  if (!res.ok) return alert(await res.text());
  await loadGame();
};

// --- Выбор: Правда / Действие / Случайно ---
async function choose(choice) {
  const res = await fetch(`/api/games/${state.gameId}/choose`, {
    method: 'POST', headers: authHeaders(),
    body: JSON.stringify({ choice })
  });
  if (!res.ok) return alert(await res.text());
  const data = await res.json();
  if (data.promptsExhausted) {
    await loadGame();
    return;
  }
  await loadGame();
}

// --- Ответить (Правда) ---
async function answerTurn(turnId) {
  const el = document.getElementById(`answer-${turnId}`);
  if (!el || !el.value.trim()) return alert('Напиши ответ.');
  const res = await fetch(`/api/turns/${turnId}/answer`, {
    method: 'POST', headers: authHeaders(), body: JSON.stringify({ answerText: el.value })
  });
  if (!res.ok) return alert(await res.text());
  await loadGame();
}

// --- Выполнено (Действие) ---
async function doneTurn(turnId) {
  const el = document.getElementById(`answer-${turnId}`);
  const comment = el ? el.value.trim() : '';
  const res = await fetch(`/api/turns/${turnId}/done`, {
    method: 'POST', headers: authHeaders(),
    body: JSON.stringify({ comment })
  });
  if (!res.ok) return alert(await res.text());
  await loadGame();
}

// --- Пропустить ---
async function skipTurn(turnId) {
  if (!confirm('Пропустить ход?')) return;
  const res = await fetch(`/api/turns/${turnId}/skip`, { method: 'POST', headers: authHeaders() });
  if (!res.ok) return alert(await res.text());
  await loadGame();
}

// --- Загрузить игру ---
async function loadGame() {
  if (!state.gameId) return;
  const res = await fetch(`/api/games/${state.gameId}`, { headers: authHeaders() });
  if (!res.ok) return alert(await res.text());
  const game = await res.json();

  // Скрыть лобби, показать игру
  $('lobbyCard').style.display = 'none';
  $('gameCard').classList.remove('hidden');

  // Неоновая надпись категории
  $('categoryLabel').textContent = categoryNames[game.category] || game.category;

  // Ссылка-приглашение
  if (state.inviteUrl) {
    $('inviteLink').style.display = '';
  } else {
    $('inviteLink').style.display = 'none';
  }

  $('players').innerHTML = '<b>Игроки:</b> ' + game.players.map(p => `${p.displayName} (${genderLabel(p.gender)})`).join(', ');

  const isMyTurn = game.currentPlayerId === state.user?.id;
  const hasOpenTurn = game.turns.some(t => t.status === 'Open');
  const gameActive = game.status === 'Active';
  const promptsExhausted = game.promptsExhausted;

  // Чей ход
  if (gameActive && !promptsExhausted && !hasOpenTurn && game.currentPlayerId) {
    const currentPlayer = game.players.find(p => p.userId === game.currentPlayerId);
    $('whoseTurn').textContent = isMyTurn ? '🎯 Твой ход!' : `⏳ Ходит ${currentPlayer?.displayName || '?'}...`;
    $('whoseTurn').style.display = '';
  } else {
    $('whoseTurn').style.display = 'none';
  }

  // Панель выбора
  if (promptsExhausted || !gameActive) {
    $('choicePanel').style.display = 'none';
  } else {
    $('choicePanel').style.display = (isMyTurn && !hasOpenTurn) ? '' : 'none';
  }

  // Статус игры
  if (game.status === 'Ended') {
    $('gameStatus').innerHTML = '<p class="muted"><b>🏁 Игра завершена.</b></p>';
    $('endGameBtn').style.display = 'none';
    $('choicePanel').style.display = 'none';
    $('whoseTurn').style.display = 'none';
  } else if (promptsExhausted && !hasOpenTurn) {
    $('gameStatus').innerHTML = '<p class="exhausted-msg"><b>🎉 Все вопросы и задания пройдены! Игра окончена.</b></p>';
    $('choicePanel').style.display = 'none';
    $('whoseTurn').style.display = 'none';
  } else {
    $('gameStatus').innerHTML = '';
    $('endGameBtn').style.display = '';
  }

  // Чат (от старых к новым)
  const sortedTurns = [...game.turns].sort((a, b) => new Date(a.createdAt) - new Date(b.createdAt));
  $('chatArea').innerHTML = sortedTurns.map(t => renderTurn(t)).join('');
  setTimeout(() => { $('chatArea').scrollTop = $('chatArea').scrollHeight; }, 50);

  await connectSignalR();
}

function renderTurn(t) {
  const isMine = state.user && t.playerUserId === state.user.id;
  const alignment = isMine ? 'mine' : 'theirs';

  let statusBadge = '';
  if (t.status === 'Skipped') statusBadge = '<span class="badge skip">Пропущено</span>';
  else if (t.status === 'TimedOut') statusBadge = '<span class="badge timeout">Время вышло</span>';
  else if (t.status === 'Answered') statusBadge = '<span class="badge answered">✓</span>';
  else statusBadge = '<span class="badge open">Ожидает</span>';

  let actions = '';
  if (t.status === 'Open' && isMine) {
    if (t.promptType === 'Truth') {
      actions = `<div class="turn-actions">
        <textarea id="answer-${t.id}" placeholder="Твой ответ..."></textarea>
        <div class="action-row">
          <button onclick="answerTurn('${t.id}')">Ответить</button>
          <button class="secondary" onclick="skipTurn('${t.id}')">Пропустить</button>
        </div>
      </div>`;
    } else {
      actions = `<div class="turn-actions">
        <textarea id="answer-${t.id}" placeholder="Комментарий (необязательно)..."></textarea>
        <div class="action-row">
          <button onclick="doneTurn('${t.id}')">✓ Выполнено</button>
          <button class="secondary" onclick="skipTurn('${t.id}')">Пропустить</button>
        </div>
      </div>`;
    }
  }

  return `<div class="chat-bubble ${alignment}">
    <div class="bubble-header">${esc(t.playerName || '?')} · <b>${t.promptType === 'Truth' ? 'Правда' : 'Действие'}</b> ${statusBadge}</div>
    <p class="prompt-text">${esc(t.promptText)}</p>
    ${t.answerText ? `<p class="answer-text">${esc(t.answerText)}</p>` : ''}
    ${actions}
  </div>`;
}

function genderLabel(g) {
  if (g === 'Male') return 'м';
  if (g === 'Female') return 'ж';
  return '';
}

function esc(text) {
  return (text || '').replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
}

// --- SignalR ---
async function connectSignalR() {
  if (connection) return;
  connection = new signalR.HubConnectionBuilder().withUrl('/hubs/game').withAutomaticReconnect().build();
  connection.on('TurnCreated', () => loadGame());
  connection.on('TurnAnswered', () => loadGame());
  connection.on('TurnSkipped', () => loadGame());
  connection.on('PlayerJoined', () => loadGame());
  connection.on('GameEnded', () => loadGame());
  await connection.start();
  await connection.invoke('JoinGame', state.gameId);
}

renderUser();
