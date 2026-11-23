import { expect, Page, test } from '@playwright/test';
import { closeContextGracefully } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

async function addCard(page: Page, columnName: string, text: string): Promise<void> {
  const column = page.getByRole('region', { name: columnName, exact: true });
  await column.getByLabel('Новая карточка').fill(text);
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await expect(column.getByText(text, { exact: true })).toBeVisible();
}

test('M9 two participants close a meeting and download the same PDF from a permanent URL', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();

  await login(first, origin, 'm9-first', 'Первый M9');
  await login(second, origin, 'm9-second', 'Второй M9');
  await first.goto(`${origin}/meetings/new`);
  await first.getByLabel('Название встречи').fill('Ретро M9 завершение');
  await first.getByRole('button', { name: 'Создать встречу' }).click();
  await first.getByRole('button', { name: 'Показать приглашение' }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await first.getByRole('button', { name: 'Начать сбор' }).click();
  await expect(second.getByText('Сбор карточек', { exact: true })).toBeVisible();
  await addCard(first, 'Начать', 'Тема A');
  await addCard(second, 'Начать', 'Тема B');
  await addCard(first, 'Перестать', 'Нулевая тема C');
  await first.getByRole('button', { name: 'Перейти к группировке' }).click();
  await expect(second.getByText('Группировка', { exact: true })).toBeVisible();

  const secondCard = second.locator('.card').filter({ has: second.getByText('Тема B', { exact: true }) });
  await secondCard.getByLabel('Назначение переноса').selectOption({ label: 'Карточка: Тема A' });
  await secondCard.getByRole('button', { name: 'Переместить', exact: true }).click();
  const group = first.locator('.card-group').filter({ hasText: 'Тема A' });
  await expect(group.locator('.card')).toHaveCount(2);
  await group.getByLabel('Название группы').fill('Общая тема AB');
  await group.getByRole('button', { name: 'Переименовать группу' }).click();
  await expect(second.locator('.card-group').filter({ hasText: 'Общая тема AB' })).toBeVisible();
  await first.getByRole('button', { name: 'Открыть голосование' }).click();
  await expect(second.getByText('Голосование', { exact: true })).toBeVisible();
  const groupTopic = second.locator('.vote-topic').filter({ hasText: 'Общая тема AB' });
  await groupTopic.getByRole('button', { name: 'Проголосовать: Общая тема AB' }).click();
  await expect(groupTopic).toContainText('Ваши голоса: 1');
  await expect(first.locator('.vote-topic').filter({ hasText: 'Общая тема AB' })).toContainText('Всего голосов: 1');
  await first.getByRole('button', { name: 'Перейти к обсуждению' }).click();
  await expect(second.getByRole('heading', { name: 'Текущая тема: Общая тема AB' })).toBeVisible();

  await first.getByLabel('Новое решение').fill('Решение M9');
  await first.getByRole('button', { name: 'Добавить решение' }).click();
  await first.getByLabel('Новый комментарий').fill('Комментарий M9');
  await first.getByRole('button', { name: 'Добавить комментарий' }).click();
  await first.getByLabel('Новая задача').fill('Задача M9');
  await first.getByLabel('Исполнитель новой задачи').selectOption({ label: 'Второй M9' });
  await first.getByRole('button', { name: 'Добавить задачу' }).click();
  await expect(second.getByText('Задача M9', { exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Отметить обсуждённой' }).click();
  await expect(second.locator('.discussion-queue-item').first()).toContainText('Обсуждена');

  first.once('dialog', (dialog) => dialog.accept());
  await first.getByRole('button', { name: 'Завершить ретро' }).click();
  await expect(first.locator('.eyebrow')).toHaveText('Завершена');
  await expect(second.locator('.eyebrow')).toHaveText('Завершена');
  await expect(second.getByRole('heading', { name: 'Итоги встречи' })).toBeVisible();
  await expect(second.locator('.closed-summary')).toContainText('Участников: 2. Карточек: 3. Тем: 2. Голосов: 1.');
  await expect(second.locator('.closed-results')).toContainText('Общая тема AB');
  await expect(second.locator('.closed-results')).toContainText('Нулевая тема C');
  await expect(second.locator('.closed-results')).toContainText('Всего голосов: 0');
  await expect(second.locator('.closed-results')).toContainText('Решение M9');
  await expect(second.locator('.closed-results')).toContainText('Комментарий M9');
  await expect(second.locator('.closed-results')).toContainText('Задача M9');
  await expect(second.getByText('Исполнитель: Второй M9', { exact: true })).toBeVisible();
  await expect(second.getByRole('button', { name: 'Добавить решение' })).toHaveCount(0);
  await expect(second.getByRole('button', { name: 'Добавить комментарий' })).toHaveCount(0);
  await expect(second.getByRole('button', { name: 'Добавить задачу' })).toHaveCount(0);
  await expect(second.getByLabel('Я готов')).toHaveCount(0);
  await expect(second.getByText('Личный остаток')).toHaveCount(0);

  const reportUrl = await second.getByRole('link', { name: 'Скачать PDF' }).getAttribute('href');
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

  const thirdContext = await browser.newContext();
  const third = await thirdContext.newPage();
  await login(third, origin, 'm9-second', 'Второй M9');
  await third.goto(first.url());
  await expect(third.locator('.eyebrow')).toHaveText('Завершена');
  const thirdResponse = await thirdContext.request.get(`${origin}${reportUrl}`);
  expect(thirdResponse.status()).toBe(200);
  expect(firstPdf.equals(Buffer.from(await thirdResponse.body()))).toBe(true);

  await Promise.all([firstContext, secondContext, thirdContext].map(closeContextGracefully));
});
