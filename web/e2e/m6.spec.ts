import { expect, Page, test } from '@playwright/test';

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

async function voteTwice(page: Page, topicTitle: string): Promise<void> {
  const topic = page.locator('.vote-topic').filter({ has: page.getByRole('heading', { name: topicTitle, exact: true }) });
  const button = topic.getByRole('button', { name: `Проголосовать: ${topicTitle}` });
  await button.click();
  await expect(topic).toContainText('Ваши голоса: 1');
  await button.click();
  await expect(topic).toContainText('Ваши голоса: 2');
}

test('M6 ranked queue, shared navigation, decisions, comments and actions work in two contexts', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();

  await login(first, origin, 'm6-first', 'Первый M6');
  await login(second, origin, 'm6-second', 'Второй M6');
  await first.goto(`${origin}/meetings/new`);
  await first.getByLabel('Название встречи').fill('Ретро M6 обсуждение');
  await first.getByRole('button', { name: 'Создать встречу' }).click();
  await first.getByRole('button', { name: 'Показать приглашение' }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await first.getByRole('button', { name: 'Начать сбор' }).click();
  await expect(second.getByText('Сбор карточек', { exact: true })).toBeVisible();
  await addCard(first, 'Начать', 'Тема A');
  await addCard(first, 'Перестать', 'Тема B');
  await addCard(first, 'Начать', 'Тема C');
  await first.getByRole('button', { name: 'Перейти к группировке' }).click();
  await expect(second.getByText('Группировка', { exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Открыть голосование' }).click();
  await expect(second.getByText('Голосование', { exact: true })).toBeVisible();
  await voteTwice(second, 'Тема A');
  await voteTwice(first, 'Тема B');
  await first.getByRole('button', { name: 'Перейти к обсуждению' }).click();

  await expect(second.getByRole('heading', { name: 'Рейтинг тем' })).toBeVisible();
  const secondQueue = second.locator('.discussion-queue-item');
  await expect(secondQueue).toHaveCount(3);
  await expect(secondQueue.nth(0)).toContainText('Тема A');
  await expect(secondQueue.nth(0)).toContainText('Всего голосов: 2');
  await expect(secondQueue.nth(1)).toContainText('Тема B');
  await expect(secondQueue.nth(1)).toContainText('Всего голосов: 2');
  await expect(secondQueue.nth(2)).toContainText('Тема C');
  await expect(secondQueue.nth(2)).toContainText('Всего голосов: 0');
  await expect(second.getByRole('heading', { name: 'Текущая тема: Тема A' })).toBeVisible();

  await first.getByRole('button', { name: 'Пропустить и далее' }).click();
  await expect(second.getByRole('heading', { name: 'Текущая тема: Тема B' })).toBeVisible();
  await expect(secondQueue.nth(0)).toContainText('Пропущена');
  await expect(second.locator('.current-topic-cards .card')).toHaveCount(1);
  await expect(second.locator('.current-topic-cards')).toContainText('Тема B');
  await expect(second.getByRole('button', { name: 'Добавить решение' })).toHaveCount(0);
  await expect(second.getByRole('button', { name: 'Добавить комментарий' })).toHaveCount(0);
  await expect(second.getByRole('button', { name: 'Добавить задачу' })).toHaveCount(0);

  await first.getByLabel('Новое решение').fill('Первоначальное решение');
  await first.getByRole('button', { name: 'Добавить решение' }).click();
  await expect(second.getByText('Первоначальное решение', { exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Изменить решение' }).click();
  await first.getByLabel('Текст решения').fill('Решение по теме B');
  await first.getByRole('button', { name: 'Сохранить решение' }).click();
  await expect(second.getByText('Решение по теме B', { exact: true })).toBeVisible();
  await expect(second.getByText('Первоначальное решение', { exact: true })).toHaveCount(0);

  await first.getByLabel('Новый комментарий').fill('Комментарий по теме B');
  await first.getByRole('button', { name: 'Добавить комментарий' }).click();
  await expect(second.getByText('Комментарий по теме B', { exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Изменить комментарий' }).click();
  await first.getByLabel('Текст комментария').fill('Обновлённый комментарий');
  await first.getByRole('button', { name: 'Сохранить комментарий' }).click();
  await expect(second.getByText('Обновлённый комментарий', { exact: true })).toBeVisible();

  await first.getByLabel('Новая задача').fill('Задача без исполнителя');
  await first.getByRole('button', { name: 'Добавить задачу' }).click();
  await expect(second.getByText('Задача без исполнителя', { exact: true })).toBeVisible();
  await expect(second.getByText('Исполнитель: Без исполнителя', { exact: true })).toBeVisible();
  await first.getByLabel('Новая задача').fill('Задача с исполнителем');
  await first.getByLabel('Исполнитель новой задачи').selectOption({ label: 'Второй M6' });
  await first.getByRole('button', { name: 'Добавить задачу' }).click();
  await expect(second.getByText('Задача с исполнителем', { exact: true })).toBeVisible();
  await expect(second.getByText('Исполнитель: Второй M6', { exact: true })).toBeVisible();
  await expect(second.getByText('Jira: не экспортировано', { exact: true })).toHaveCount(2);

  const assignedAction = first.locator('.discussion-entry').filter({ hasText: 'Задача с исполнителем' });
  await assignedAction.getByRole('button', { name: 'Изменить задачу' }).click();
  await first.getByLabel('Текст задачи').fill('Обновлённая задача с исполнителем');
  await first.getByRole('button', { name: 'Сохранить задачу' }).click();
  await expect(second.getByText('Обновлённая задача с исполнителем', { exact: true })).toBeVisible();
  await expect(second.getByText('Исполнитель: Второй M6', { exact: true })).toBeVisible();

  await first.getByRole('button', { name: 'Обсуждена, далее' }).click();
  await expect(second.getByRole('heading', { name: 'Текущая тема: Тема C' })).toBeVisible();
  await expect(secondQueue.nth(1)).toContainText('Обсуждена');
  await first.locator('.discussion-queue-item').nth(0).getByRole('button', { name: 'Выбрать тему: Тема A' }).click();
  await expect(second.getByRole('heading', { name: 'Текущая тема: Тема A' })).toBeVisible();
  await expect(first.getByRole('heading', { name: 'Текущая тема: Тема A' })).toBeVisible();
  await expect(secondQueue.nth(0)).toContainText('Пропущена');

  await Promise.all([firstContext.close(), secondContext.close()]);
});
