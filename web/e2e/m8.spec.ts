import { expect, Page, test } from '@playwright/test';
import { advanceBeforeEveryoneIsReady, closeContextGracefully } from './helpers';

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
  await first.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await first.getByLabel('Название встречи').fill('Ретро M8 mock Jira');
  await first.getByRole('button', { name: 'Создать ретро', exact: true }).click();
  await expect(first.getByRole('heading', { name: 'Ретро M8 mock Jira' })).toBeVisible();
  await first.getByRole('button', { name: 'Пригласить', exact: true }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await first.getByRole('button', { name: 'Закрыть приглашение' }).click();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Сбор карточек');
  const column = first.getByRole('region', { name: 'Начать', exact: true });
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await column.getByLabel('Новая карточка').fill('Тема для экспорта');
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await expect(column.getByText('Тема для экспорта', { exact: true })).toBeVisible();
  await advanceBeforeEveryoneIsReady(first, 'Перейти к группировке');
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Группировка');
  await advanceBeforeEveryoneIsReady(first, 'Открыть голосование');
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Голосование');
  await advanceBeforeEveryoneIsReady(first, 'Перейти к обсуждению');
  await expect(second.getByRole('heading', { name: 'Тема для экспорта' })).toBeVisible();

  await first.getByRole('button', { name: 'Задача', exact: true }).click();
  await first.getByLabel('Краткое название').fill('Задача для mock Jira');
  await first.getByLabel('Исполнитель').selectOption({ label: 'Второй M8' });
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
  await expect(first.getByRole('button', { name: 'Экспортировать в mock Jira' })).toHaveCount(0);
  await expect(first.getByRole('button', { name: 'Изменить: Задача для mock Jira' })).toBeDisabled();
  await expect(first.getByRole('button', { name: 'Удалить: Задача для mock Jira' })).toBeDisabled();
  await expect(second.getByRole('button', { name: 'Изменить: Задача для mock Jira' })).toHaveCount(0);

  await Promise.all([firstContext, secondContext].map(closeContextGracefully));
});
