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

async function voteTwice(page: Page, topicTitle: string): Promise<void> {
  const topic = voteControls(page, topicTitle);
  const button = topic.getByRole('button', { name: `Проголосовать: ${topicTitle}` });
  await button.click();
  await expect(topic.getByLabel('Ваши голоса')).toHaveText('1');
  await button.click();
  await expect(topic.getByLabel('Ваши голоса')).toHaveText('2');
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
  await first.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await first.getByLabel('Название встречи').fill('Ретро M6 обсуждение');
  await first.getByRole('button', { name: 'Создать ретро', exact: true }).click();
  await first.getByRole('button', { name: 'Пригласить', exact: true }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await first.getByRole('button', { name: 'Закрыть приглашение' }).click();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Сбор карточек');
  await addCard(first, 'Начать', 'Тема A');
  await addCard(first, 'Перестать', 'Тема B');
  await addCard(first, 'Начать', 'Тема C');
  await advanceBeforeEveryoneIsReady(first, 'Перейти к группировке');
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Группировка');
  await advanceBeforeEveryoneIsReady(first, 'Открыть голосование');
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Голосование');
  await voteTwice(second, 'Тема A');
  await voteTwice(first, 'Тема B');
  await advanceBeforeEveryoneIsReady(first, 'Перейти к обсуждению');

  const agenda = second.getByRole('navigation', { name: 'Темы по голосам' });
  const secondQueue = agenda.getByRole('button');
  const firstWorkspace = first.locator('.discussion-workspace');
  const secondWorkspace = second.locator('.discussion-workspace');
  await expect(secondQueue).toHaveCount(3);
  await expect(second.locator('.board')).toHaveCount(0);
  await expect(secondQueue.nth(0)).toContainText('Тема A');
  await expect(secondQueue.nth(0)).toContainText('Всего голосов: 2');
  await expect(secondQueue.nth(1)).toContainText('Тема B');
  await expect(secondQueue.nth(1)).toContainText('Всего голосов: 2');
  await expect(secondQueue.nth(2)).toContainText('Тема C');
  await expect(secondQueue.nth(2)).toContainText('Всего голосов: 0');
  await expect(secondWorkspace.getByRole('heading', { name: 'Тема A', exact: true })).toBeVisible();
  const agendaBox = (await agenda.boundingBox())!;
  const workspaceBox = (await secondWorkspace.boundingBox())!;
  expect(agendaBox.x + agendaBox.width).toBeLessThan(workspaceBox.x);

  await first.getByRole('button', { name: 'Далее', exact: true }).click();
  await expect(secondWorkspace.getByRole('heading', { name: 'Тема B', exact: true })).toBeVisible();
  await expect(secondQueue.nth(0)).toHaveClass(/discussed-topic/);
  await expect(secondQueue.nth(0)).toContainText('Обсуждена');
  await expect(second.getByRole('button', { name: 'Решение', exact: true })).toHaveCount(0);
  await expect(second.getByRole('button', { name: 'Далее', exact: true })).toHaveCount(0);

  await secondQueue.nth(2).click();
  await expect(secondWorkspace.getByRole('heading', { name: 'Тема C', exact: true })).toBeVisible();
  await expect(firstWorkspace.getByRole('heading', { name: 'Тема B', exact: true })).toBeVisible();
  await second.getByLabel('Новый комментарий').fill('Черновик для темы C');
  await first.getByRole('button', { name: 'Выбрать тему: Тема B' }).click();
  await expect(secondWorkspace.getByRole('heading', { name: 'Тема B', exact: true })).toBeVisible();
  await expect(second.getByLabel('Новый комментарий')).toHaveValue('');

  await second.getByLabel('Новый комментарий').fill('Комментарий участника');
  await second.getByRole('button', { name: 'Добавить комментарий' }).click();
  await expect(firstWorkspace.getByText('Комментарий участника', { exact: true })).toBeVisible();

  const taskMode = second.getByRole('button', { name: 'Задача', exact: true });
  await taskMode.click();
  await expect(taskMode).toHaveAttribute('aria-pressed', 'true');
  await second.getByLabel('Краткое название').fill('Задача с исполнителем');
  await second.getByLabel('Описание').fill('Подробности задачи');
  await second.getByLabel('Исполнитель').selectOption({ label: 'Второй M6' });
  await second.getByRole('button', { name: 'Добавить задачу' }).click();
  await expect(firstWorkspace.getByText('Подробности задачи', { exact: true })).toBeVisible();
  await expect(firstWorkspace.getByText('Исполнитель: Второй M6', { exact: true })).toBeVisible();
  const entries = firstWorkspace.locator('.discussion-feed > li');
  await expect(entries.nth(0)).toContainText('Комментарий участника');
  await expect(entries.nth(1)).toContainText('Задача с исполнителем');

  const assignedAction = entries.nth(1);
  await assignedAction.getByRole('button', { name: 'Изменить: Задача с исполнителем' }).click();
  await assignedAction.getByLabel('Краткое название').fill('Обновлённая задача');
  await assignedAction.getByLabel('Описание').fill('Обновлённое описание');
  await assignedAction.getByRole('button', { name: 'Сохранить задачу' }).click();
  await expect(secondWorkspace.getByText('Обновлённая задача', { exact: true })).toBeVisible();
  await expect(secondWorkspace.getByText('Обновлённое описание', { exact: true })).toBeVisible();

  await first.getByRole('button', { name: 'Решение', exact: true }).click();
  await first.getByLabel('Новое решение').fill('Решение по теме B');
  await first.getByRole('button', { name: 'Добавить решение' }).click();
  await expect(secondWorkspace.getByText('Решение по теме B', { exact: true })).toBeVisible();
  await expect(entries.nth(2)).toContainText('Решение по теме B');

  await first.getByRole('button', { name: 'Далее', exact: true }).click();
  await expect(secondWorkspace.getByRole('heading', { name: 'Тема C', exact: true })).toBeVisible();
  await expect(second.getByLabel('Новый комментарий')).toHaveValue('Черновик для темы C');
  await expect(secondWorkspace.getByText('Комментарий участника', { exact: true })).toHaveCount(0);
  await expect(first.getByRole('button', { name: 'Далее', exact: true })).toBeDisabled();

  await first.getByRole('button', { name: 'Выбрать тему: Тема A' }).click();
  await expect(secondWorkspace.getByRole('heading', { name: 'Тема A', exact: true })).toBeVisible();
  await expect(secondQueue.nth(0)).toHaveAttribute('aria-current', 'true');
  await expect(secondQueue.nth(0)).toHaveClass(/discussed-topic/);

  await first.getByRole('button', { name: 'Выбрать тему: Тема B' }).click();
  await expect(secondWorkspace.getByText('Обновлённое описание', { exact: true })).toBeVisible();
  await second.reload();
  await expect(secondWorkspace.getByText('Комментарий участника', { exact: true })).toBeVisible();
  await expect(secondWorkspace.getByText('Обновлённое описание', { exact: true })).toBeVisible();

  await second.setViewportSize({ width: 390, height: 844 });
  const mobileAgenda = (await agenda.boundingBox())!;
  const mobileWorkspace = (await secondWorkspace.boundingBox())!;
  expect(mobileAgenda.y + mobileAgenda.height).toBeLessThan(mobileWorkspace.y);
  expect(mobileWorkspace.x + mobileWorkspace.width).toBeLessThanOrEqual(390);

  await Promise.all([firstContext, secondContext].map(closeContextGracefully));
});
