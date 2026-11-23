import { expect, Page, test } from '@playwright/test';
import { closeContextGracefully } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

test('M8 mock Jira export shares created status and locks the action in two contexts', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();

  await login(first, origin, 'm8-first', 'Первый M8');
  await login(second, origin, 'm8-second', 'Второй M8');
  await first.goto(`${origin}/meetings/new`);
  await first.getByLabel('Название встречи').fill('Ретро M8 mock Jira');
  await first.getByRole('button', { name: 'Создать встречу' }).click();
  await expect(first.getByRole('heading', { name: 'Ретро M8 mock Jira' })).toBeVisible();
  await first.getByRole('button', { name: 'Показать приглашение' }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await first.getByRole('button', { name: 'Начать сбор' }).click();
  await expect(second.getByText('Сбор карточек', { exact: true })).toBeVisible();
  const column = first.getByRole('region', { name: 'Начать', exact: true });
  await column.getByLabel('Новая карточка').fill('Тема для экспорта');
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await expect(column.getByText('Тема для экспорта', { exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Перейти к группировке' }).click();
  await expect(second.getByText('Группировка', { exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Открыть голосование' }).click();
  await expect(second.getByText('Голосование', { exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Перейти к обсуждению' }).click();
  await expect(second.getByRole('heading', { name: 'Текущая тема: Тема для экспорта' })).toBeVisible();

  await first.getByLabel('Новая задача').fill('Задача для mock Jira');
  await first.getByLabel('Исполнитель новой задачи').selectOption({ label: 'Второй M8' });
  await first.getByRole('button', { name: 'Добавить задачу' }).click();
  await expect(second.getByText('Задача для mock Jira', { exact: true })).toBeVisible();
  await expect(second.getByText('Исполнитель: Второй M8', { exact: true })).toBeVisible();
  await expect(second.getByText('Jira: не экспортировано', { exact: true })).toBeVisible();
  await expect(second.getByRole('button', { name: 'Экспортировать в mock Jira' })).toHaveCount(0);

  await first.getByRole('button', { name: 'Экспортировать в mock Jira' }).click();
  await expect(second.getByText('Mock Jira: создано', { exact: true })).toBeVisible();
  const issueLink = second.getByRole('link', { name: /^MOCK-/ });
  await expect(issueLink).toBeVisible();
  await expect(issueLink).toHaveAttribute('href', /^https:\/\/jira\.local\.invalid\/browse\/MOCK-/);
  await expect(second.getByText(/externalRequestId: [0-9a-f-]{36}/)).toBeVisible();
  await expect(first.getByRole('button', { name: 'Экспортировать в mock Jira' })).toHaveCount(0);
  await expect(first.getByRole('button', { name: 'Изменить задачу' })).toBeDisabled();
  await expect(first.getByRole('button', { name: 'Удалить задачу' })).toBeDisabled();
  await expect(second.getByRole('button', { name: 'Изменить задачу' })).toHaveCount(0);

  await Promise.all([firstContext, secondContext].map(closeContextGracefully));
});
