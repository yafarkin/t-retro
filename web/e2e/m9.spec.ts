import { expect, Page, test } from '@playwright/test';
import { advanceBeforeEveryoneIsReady, closeContextGracefully, voteControls } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

async function addCard(page: Page, columnName: string, text: string): Promise<void> {
  const column = page.getByRole('region', { name: columnName, exact: true });
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await column.getByLabel('Новая карточка').fill(text);
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await expect(column.getByText(text, { exact: true })).toBeVisible();
}

test('M9 results board, read-only discussion, permanent PDF and facilitator archive', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();

  await login(first, origin, 'm9-first', 'Первый M9');
  await login(second, origin, 'm9-second', 'Второй M9');
  await first.goto(`${origin}/meetings/new`);
  await first.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await first.getByLabel('Название встречи').fill('Ретро M9 завершение');
  await first.getByRole('button', { name: 'Создать ретро', exact: true }).click();
  await first.getByRole('button', { name: 'Пригласить', exact: true }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await first.getByRole('button', { name: 'Закрыть приглашение' }).click();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Сбор карточек');
  await addCard(first, 'Начать', 'Тема A');
  await addCard(second, 'Начать', 'Тема B');
  await addCard(first, 'Перестать', 'Нулевая тема C');
  await advanceBeforeEveryoneIsReady(first, 'Перейти к группировке');
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Группировка');

  const secondCard = second.locator('.card').filter({ has: second.getByText('Тема B', { exact: true }) });
  await secondCard.dragTo(second.locator('.card').filter({ has: second.getByText('Тема A', { exact: true }) }));
  const group = first.locator('.card-group').filter({ hasText: 'Тема A' });
  await group.getByRole('button', { name: 'Развернуть группу' }).click();
  await expect(group.locator('.card')).toHaveCount(2);
  await group.getByRole('button', { name: 'Переименовать группу' }).click();
  await group.getByLabel('Название группы').fill('Общая тема AB');
  await group.getByRole('button', { name: 'Сохранить название' }).click();
  await expect(second.locator('.card-group').filter({ hasText: 'Общая тема AB' })).toBeVisible();
  await advanceBeforeEveryoneIsReady(first, 'Открыть голосование');
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Голосование');
  const groupTopic = voteControls(second, 'Общая тема AB');
  await groupTopic.getByRole('button', { name: 'Проголосовать: Общая тема AB' }).click();
  await expect(groupTopic.getByLabel('Ваши голоса')).toHaveText('1');
  await expect(voteControls(first, 'Общая тема AB').getByLabel('Ваши голоса')).toHaveText('0');
  await expect(groupTopic.getByRole('button', { name: 'Расклад голосов' })).toHaveCount(0);
  await advanceBeforeEveryoneIsReady(first, 'Перейти к обсуждению');
  await expect(second.getByRole('heading', { name: 'Общая тема AB' })).toBeVisible();
  await expect(second.locator('.current-topic-cards article')).toHaveCount(2);

  await first.getByRole('button', { name: 'Решение', exact: true }).click();
  await first.getByLabel('Новое решение').fill('Решение M9');
  await first.getByRole('button', { name: 'Добавить решение' }).click();
  await first.getByRole('button', { name: 'Комментарий', exact: true }).click();
  await first.getByLabel('Новый комментарий').fill('Комментарий M9');
  await first.getByRole('button', { name: 'Добавить комментарий' }).click();
  await first.getByRole('button', { name: 'Задача', exact: true }).click();
  await first.getByLabel('Краткое название').fill('Задача M9');
  await first.getByLabel('Описание').fill('Описание задачи M9');
  await first.getByLabel('Исполнитель').selectOption({ label: 'Второй M9' });
  await first.getByRole('button', { name: 'Добавить задачу' }).click();
  await expect(second.getByText('Задача M9', { exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Отметить обсуждённой' }).click();
  await expect(second.locator('.discussion-topic-button').first()).toContainText('Обсуждена');

  const finish = first.getByRole('button', { name: 'Завершить ретро', exact: true });
  const confirmation = first.getByRole('dialog', { name: 'Завершить ретро?' });
  await finish.click();
  await expect(confirmation.getByRole('button', { name: 'Отмена' })).toBeFocused();
  await confirmation.getByRole('button', { name: 'Отмена' }).click();
  await expect(confirmation).not.toBeVisible();
  await expect(first.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Обсуждение');
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Обсуждение');
  await finish.click();
  await confirmation.getByRole('button', { name: 'Завершить ретро', exact: true }).click();
  await expect(first.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Итоги');
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Итоги');
  await expect(second.getByRole('heading', { name: 'Итоги встречи' })).toBeVisible();
  await expect(second.locator('.closed-summary')).toContainText('Участников: 2. Карточек: 3. Тем: 2. Голосов: 1.');
  const results = second.locator('.results-board');
  const discussed = results.getByRole('region', { name: 'Начать' }).locator('.card-group');
  const undiscussed = results.getByRole('region', { name: 'Перестать' }).locator('.card');
  await expect(discussed).toContainText('Общая тема AB');
  await expect(discussed).toContainText('Всего голосов: 1');
  await expect(discussed).not.toHaveClass(/undiscussed-topic/);
  await expect(undiscussed).toContainText('Нулевая тема C');
  await expect(undiscussed).toContainText('Всего голосов: 0');
  await expect(undiscussed).toHaveClass(/undiscussed-topic/);

  const entriesButton = discussed.getByRole('button', { name: 'Записи обсуждения: Общая тема AB, 3' });
  const panel = second.getByRole('dialog', { name: 'Записи обсуждения' });
  await entriesButton.click();
  await expect(panel.getByRole('heading', { name: 'Общая тема AB' })).toBeVisible();
  await expect(panel.locator('.discussion-entry')).toHaveCount(3);
  await expect(panel).toContainText('Решение M9');
  await expect(panel).toContainText('Комментарий M9');
  await expect(panel).toContainText('Задача M9');
  await expect(panel).toContainText('Описание задачи M9');
  await expect(panel).toContainText('Исполнитель: Второй M9');
  await expect(panel.getByRole('button')).toHaveText('Закрыть');
  const position = (await panel.boundingBox())!;
  expect(position.x + position.width).toBeCloseTo(second.viewportSize()!.width, 0);
  await second.keyboard.press('Escape');
  await expect(panel).not.toBeVisible();
  await expect(entriesButton).toBeFocused();
  const viewport = second.viewportSize()!;
  await second.setViewportSize({ width: 390, height: 844 });
  await undiscussed.getByRole('button', { name: 'Записи обсуждения: Нулевая тема C, 0' }).click();
  await expect(panel).toContainText('По этой теме нет записей.');
  expect((await panel.boundingBox())!.width).toBeLessThanOrEqual(390);
  await panel.getByRole('button', { name: 'Закрыть' }).click();
  await second.setViewportSize(viewport);
  await expect(second.getByRole('button', { name: 'Добавить решение' })).toHaveCount(0);
  await expect(second.getByRole('button', { name: 'Добавить комментарий' })).toHaveCount(0);
  await expect(second.getByRole('button', { name: 'Добавить задачу' })).toHaveCount(0);
  await expect(second.getByLabel('Я готов')).toHaveCount(0);
  await expect(second.getByText('Личный остаток')).toHaveCount(0);

  const pdfLink = second.locator('.meeting-toolbar').getByRole('link', { name: 'Скачать PDF' });
  await expect(pdfLink.locator('svg')).toBeVisible();
  const reportUrl = await pdfLink.getAttribute('href');
  expect(reportUrl).toMatch(/\/api\/meetings\/[0-9a-f-]+\/report\.pdf$/);
  const firstResponse = await firstContext.request.get(`${origin}${reportUrl}`);
  const secondResponse = await secondContext.request.get(`${origin}${reportUrl}`);
  expect(firstResponse.status()).toBe(200);
  expect(secondResponse.status()).toBe(200);
  expect(firstResponse.headers()['content-type']).toContain('application/pdf');
  expect(firstResponse.headers()['cache-control']).toContain('private');
  expect(firstResponse.headers()['cache-control']).toContain('no-store');
  const firstPdf = Buffer.from(await firstResponse.body());
  const secondPdf = Buffer.from(await secondResponse.body());
  expect(firstPdf.subarray(0, 5).toString()).toBe('%PDF-');
  expect(firstPdf.equals(secondPdf)).toBe(true);

  const jsonLink = second.locator('.meeting-toolbar').getByRole('link', { name: 'Скачать JSON' });
  const downloadPromise = second.waitForEvent('download');
  await jsonLink.click();
  const jsonDownload = await downloadPromise;
  expect(jsonDownload.suggestedFilename()).toBe('retro-results.json');
  expect(await jsonDownload.failure()).toBeNull();

  const meetingUrl = first.url();
  const archiveUrl = new URL(meetingUrl);
  archiveUrl.pathname = '/api' + archiveUrl.pathname + '/archive';
  const stateUrl = new URL(archiveUrl);
  stateUrl.pathname = stateUrl.pathname.replace(/archive$/, 'state');
  const version = (await (await firstContext.request.get(stateUrl.href)).json()).version;
  await expect(second.getByRole('button', { name: 'Архивировать ретро' })).toHaveCount(0);
  const forbidden = await secondContext.request.put(archiveUrl.href, { data: { expectedVersion: version } });
  expect(forbidden.status()).toBe(403);
  const stale = await firstContext.request.put(archiveUrl.href, { data: { expectedVersion: version - 1 } });
  expect(stale.status()).toBe(409);
  await first.getByRole('button', { name: 'Архивировать ретро' }).click();
  await expect(first.getByText('В архиве', { exact: true })).toBeVisible();
  const replay = await firstContext.request.put(archiveUrl.href, { data: { expectedVersion: version } });
  expect(await replay.json()).toEqual({ version: version + 1 });

  let archiveRequests = 0;
  first.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/meetings/archive') {
      archiveRequests++;
    }
  });
  await Promise.all([first.goto(origin), second.goto(origin)]);
  const archivedTitle = 'Ретро M9 завершение';
  for (const page of [first, second]) {
    await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
    await expect(page.getByText('Загружаем встречи…')).toHaveCount(0);
    await expect(page.getByRole('link', { name: archivedTitle })).toHaveCount(0);
    await expect(page.locator('details.archive')).not.toHaveAttribute('open');
  }
  expect(archiveRequests).toBe(0);
  await first.getByText('Архив ретро', { exact: true }).click();
  const archivedLink = first.locator('details.archive').getByRole('link', { name: archivedTitle });
  await expect(archivedLink).toBeVisible();
  expect(archiveRequests).toBe(1);
  const memberArchive = second.waitForResponse((response) => new URL(response.url()).pathname === '/api/meetings/archive');
  await second.getByText('Архив ретро', { exact: true }).click();
  expect(await (await memberArchive).json()).toEqual([]);
  await expect(second.getByText('В архиве пока нет встреч.')).toBeVisible();
  await archivedLink.click();
  await expect(first.getByText('В архиве', { exact: true })).toBeVisible();

  const thirdContext = await browser.newContext();
  const third = await thirdContext.newPage();
  await login(third, origin, 'm9-second', 'Второй M9');
  await third.goto(meetingUrl);
  await expect(third.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Итоги');
  const thirdResponse = await thirdContext.request.get(`${origin}${reportUrl}`);
  expect(thirdResponse.status()).toBe(200);
  expect(firstPdf.equals(Buffer.from(await thirdResponse.body()))).toBe(true);

  await Promise.all([firstContext, secondContext, thirdContext].map(closeContextGracefully));
});
