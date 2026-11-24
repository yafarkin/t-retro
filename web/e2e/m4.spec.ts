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

test('M4 manual groups, live moves, stub preview and drag and drop work in two contexts', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();

  await login(first, origin, 'm4-first', 'Первый M4');
  await login(second, origin, 'm4-second', 'Второй M4');
  await first.goto(`${origin}/meetings/new`);
  await first.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await first.getByLabel('Название встречи').fill('Ретро M4 группы');
  await first.getByRole('button', { name: 'Создать встречу' }).click();
  await first.getByRole('button', { name: 'Показать приглашение' }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await first.getByRole('button', { name: 'Начать сбор' }).click();
  await expect(second.getByText('Сбор карточек', { exact: true })).toBeVisible();

  await addCard(first, 'Начать', 'Первая A');
  await addCard(second, 'Перестать', 'Вторая B');
  await addCard(first, 'Начать', 'Третья C');
  await first.getByRole('button', { name: 'Перейти к группировке' }).click();
  await expect(second.getByText('Группировка', { exact: true })).toBeVisible();

  const secondCard = second.locator('.card').filter({ has: second.getByText('Вторая B', { exact: true }) });
  await secondCard.getByLabel('Назначение переноса').selectOption({ label: 'Карточка: Первая A' });
  await secondCard.getByRole('button', { name: 'Переместить', exact: true }).click();
  const firstGroup = first.locator('.card-group').filter({ hasText: 'Первая A' });
  await expect(firstGroup).toBeVisible();
  await expect(firstGroup.locator('.card')).toHaveCount(2);

  await firstGroup.getByLabel('Название группы').fill('Общая тема');
  await firstGroup.getByRole('button', { name: 'Переименовать группу' }).click();
  const secondGroup = second.locator('.card-group').filter({ hasText: 'Общая тема' });
  await expect(secondGroup).toBeVisible();
  const extractedCard = secondGroup.locator('.card').filter({ has: second.getByText('Первая A', { exact: true }) });
  await extractedCard.getByLabel('Назначение переноса').selectOption({ label: 'Колонка: Начать' });
  await extractedCard.getByRole('button', { name: 'Переместить', exact: true }).click();
  await expect(second.locator('.card-group')).toHaveCount(0);
  await expect(first.locator('.card-group')).toHaveCount(0);

  await first.getByRole('button', { name: 'Предложить группировку' }).click();
  await expect(first.getByRole('heading', { name: 'Предпросмотр тестовой заглушки' })).toBeVisible();
  await expect(first.locator('.proposal')).toContainText('Тестовая группа 1');
  await first.getByRole('button', { name: 'Применить предложение' }).click();
  const appliedOnSecond = second.locator('.card-group').filter({ hasText: 'Тестовая группа 1' });
  await expect(appliedOnSecond).toBeVisible();
  await expect(appliedOnSecond.locator('.card')).toHaveCount(2);

  await appliedOnSecond.getByLabel('Назначение переноса группы').selectOption({ label: 'Колонка: Перестать' });
  await appliedOnSecond.getByRole('button', { name: 'Переместить группу' }).click();
  const targetColumn = second.getByRole('region', { name: 'Перестать', exact: true });
  const movedGroup = targetColumn.locator('.card-group').filter({ hasText: 'Тестовая группа 1' });
  await expect(movedGroup).toBeVisible();
  const sourceColumn = second.getByRole('region', { name: 'Начать', exact: true });
  const freeCard = sourceColumn.locator('.card').filter({ has: second.getByText('Третья C', { exact: true }) });
  const dataTransfer = await second.evaluateHandle(() => new DataTransfer());
  const dragHandle = freeCard.locator('.drag-handle');
  await dragHandle.dispatchEvent('dragstart', { dataTransfer });
  await movedGroup.dispatchEvent('dragover', { dataTransfer });
  await movedGroup.dispatchEvent('drop', { dataTransfer });
  await expect(movedGroup.locator('.card')).toHaveCount(3);
  await expect(movedGroup).toContainText('Третья C');

  await Promise.all([firstContext, secondContext].map(closeContextGracefully));
});
